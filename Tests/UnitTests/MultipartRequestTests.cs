namespace Resty.Tests;

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Resty.Core.Execution;
using Resty.Core.Models;
using Resty.Core.Parsers;

public class MultipartRequestTests : IDisposable
{
  private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("resty-multipart-");

  [Fact]
  public async Task MultipartUpload_ShouldSendFormAndCaptureResponseOverHttp()
  {
    byte[] fixtureBytes = [0x89, 0x50, 0x4E, 0x47, 0x00, 0x0D, 0x0A, 0xFF, 0x80];
    var fixtures = _directory.CreateSubdirectory(Path.Combine("steps", "fixtures"));
    await File.WriteAllBytesAsync(Path.Combine(fixtures.FullName, "sample image.png"), fixtureBytes);

    var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = _directory.FullName });
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
    await using var app = builder.Build();

    app.MapGet("/setup", () => Results.Json(new {
      ownerId = 42,
      token = "fixture-token"
    }));
    app.MapPost("/owners/42/uploads", async ( HttpContext context ) =>
    {
      var request = context.Request;
      var form = await request.ReadFormAsync(context.RequestAborted);
      var file = form.Files.GetFile("attachment");
      if (file is null) {
        return Results.BadRequest("Missing attachment");
      }

      using var bytes = new MemoryStream();
      await file.CopyToAsync(bytes, context.RequestAborted);
      return Results.Json(new {
        result = new { id = "upload-456" },
        ownerId = form["OwnerId"].ToString(),
        label = form["label"].ToString(),
        authorization = request.Headers.Authorization.ToString(),
        requestContentType = request.ContentType,
        fileName = file.FileName,
        fileContentType = file.ContentType,
        bytes = bytes.ToArray()
      });
    });
    app.MapGet("/uploads/upload-456", () => Results.Ok());

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await app.StartAsync(timeout.Token);

    var suitePath = Path.Combine(_directory.FullName, "suite.resty");
    await File.WriteAllTextAsync(suitePath, $$"""
      ```yaml
      include:
        - steps/uploads.resty
      variables:
        host: {{app.Urls.Single()}}
      ```
      ```yaml
      test: setup
      get: $host/setup
      capture:
        owner_id: $.ownerId
        token: $.token
      ```
      ```yaml
      test: get_upload
      requires: upload_asset
      get: $host/uploads/$upload_id
      expect:
        status: 200
      ```
      """, timeout.Token);
    await File.WriteAllTextAsync(Path.Combine(_directory.FullName, "steps", "uploads.resty"), """
      ```yaml
      test: upload_asset
      requires: setup
      post: $host/owners/$owner_id/uploads
      headers:
        Authorization: Bearer $token
      multipart:
        - name: OwnerId
          value: $owner_id
        - name: label
          value: café
        - name: attachment
          file: fixtures/sample image.png
          content_type: image/png
      expect:
        status: 200
      capture:
        upload_id: $.result.id
      ```
      """, timeout.Token);

    using var client = new HttpClient();
    var runner = new TestSuiteRunner(new HttpTestExecutor(client));
    var summary = await runner.RunTestFilesAsync([suitePath], timeout.Token);

    Assert.Equal(3, summary.Results.Count);
    Assert.All(summary.Results, result => Assert.True(result.Passed, $"{result.Test.Name}: {result.ErrorMessage} {result.ResponseBody}"));
    var upload = Assert.Single(summary.Results, result => result.Test.Name == "upload_asset");
    Assert.Equal("upload-456", upload.ExtractedVariables["upload_id"]);

    using var received = JsonDocument.Parse(upload.ResponseBody!);
    var formData = received.RootElement;
    Assert.Equal("42", formData.GetProperty("ownerId").GetString());
    Assert.Equal("café", formData.GetProperty("label").GetString());
    Assert.Equal("Bearer fixture-token", formData.GetProperty("authorization").GetString());
    var requestContentType = formData.GetProperty("requestContentType").GetString();
    Assert.StartsWith("multipart/form-data; boundary=", requestContentType);
    Assert.Equal(requestContentType, upload.RequestInfo?.ContentType);
    Assert.Equal("sample image.png", formData.GetProperty("fileName").GetString());
    Assert.Equal("image/png", formData.GetProperty("fileContentType").GetString());
    Assert.Equal(fixtureBytes, formData.GetProperty("bytes").GetBytesFromBase64());
  }

  [Fact]
  public async Task MissingFixture_ShouldReportTestAndResolvedPathWithoutSendingRequest()
  {
    var suitePath = Path.Combine(_directory.FullName, "suite.resty");
    await File.WriteAllTextAsync(suitePath, """
      ```yaml
      test: missing_attachment
      post: http://localhost/uploads
      multipart:
        - name: attachment
          file: fixtures/missing.bin
      ```
      """);

    var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    using var client = new HttpClient(handler.Object, disposeHandler: false);
    var runner = new TestSuiteRunner(new HttpTestExecutor(client));
    var summary = await runner.RunTestFilesAsync([suitePath]);

    var result = Assert.Single(summary.Results);
    Assert.False(result.Passed);
    Assert.Contains("missing_attachment", result.ErrorMessage);
    var expectedPath = Path.Combine(_directory.FullName, "fixtures", "missing.bin");
    Assert.Contains(expectedPath, result.ErrorMessage);
    Assert.Equal(expectedPath, Assert.IsType<FileNotFoundException>(result.Exception).FileName);
    Assert.Empty(handler.Invocations);
  }

  [Theory]
  [InlineData("multipart: []", "at least one part")]
  [InlineData("multipart: [{ value: example }]", "must have a name")]
  [InlineData("multipart: [{ name: sample }]", "either a value or a nonempty file path")]
  [InlineData("multipart: [{ name: sample, value: example, file: sample.bin }]", "either a value or a nonempty file path")]
  [InlineData("multipart: [{ name: sample, value: example, content_type: image/png }]", "only for a file")]
  [InlineData("multipart: [{ name: sample, value: example }]\nbody: example", "cannot be combined")]
  [InlineData("multipart: [{ name: sample, value: example }]\nheaders: { content-type: application/json }", "cannot be combined")]
  public void InvalidMultipart_ShouldFailValidation( string yaml, string error )
  {
    var markdown = $"```yaml\ntest: invalid_upload\npost: http://localhost/uploads\n{yaml}\n```";
    var block = Assert.Single(MarkdownParser.FindYamlBlocks(markdown, "invalid.resty")).Value;
    var exception = Assert.Throws<InvalidOperationException>(() => HttpTest.FromYamlBlock(block, "invalid.resty", 1));
    Assert.Contains(error, exception.Message);
  }

  public void Dispose()
  {
    _directory.Delete(recursive: true);
  }
}
