using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Kypelon.Pdf.AspNetCore;

/// <summary>Streaming ASP.NET Core integration.</summary>
public static class PdfResults
{
    /// <summary>Prepares the document once when the result executes, before response headers or body are written.</summary>
    public static IResult PdfFile(this PdfDocument document, string fileName = "document.pdf")
    {
        ArgumentNullException.ThrowIfNull(document);
        return new PdfResult(document.Prepare, fileName);
    }
    /// <summary>Streams a prepared snapshot without measuring, paginating or shaping again.</summary>
    public static IResult PdfFile(this PreparedPdfDocument document, string fileName = "document.pdf")
    {
        ArgumentNullException.ThrowIfNull(document);
        return new PdfResult(_ => document, fileName);
    }
    private sealed class PdfResult(Func<CancellationToken, PreparedPdfDocument> prepare, string fileName) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Any(char.IsControl))
                throw new ArgumentException("Filename must be nonempty and contain no control characters.", nameof(fileName));
            context.RequestAborted.ThrowIfCancellationRequested();
            var document = prepare(context.RequestAborted);
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(fileName);
            context.Response.ContentType = "application/pdf";
            context.Response.Headers.ContentDisposition = disposition.ToString();
            await document.WriteAsync(context.Response.Body, context.RequestAborted, leaveOpen: true).ConfigureAwait(false);
        }
    }
}
