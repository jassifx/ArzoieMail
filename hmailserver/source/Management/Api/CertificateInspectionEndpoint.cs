using Microsoft.AspNetCore.Http.Features;

namespace ArzoieMail.Management;

public static class CertificateInspectionEndpoint
{
    private static readonly SemaphoreSlim Slots = new(2, 2);
    public static async Task<IResult> Handle(HttpContext context, TimeProvider clock)
    {
        if (context.User.FindFirst("level")?.Value != "2") throw new ManagementError(403, "Server administration is required.");
        const int maximumBody = CertificateInspectionRequest.MaximumBodyBytes;
        if (context.Request.ContentLength > maximumBody) throw new ManagementError(413, "Certificate inspection requests must not exceed 1 MiB.");
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is null || feature.IsReadOnly) throw new ManagementError(503, "This host cannot accept protected certificate uploads.");
        feature.MaxRequestBodySize = maximumBody; // All other routes retain the 16 KiB limit.
        if (!await Slots.WaitAsync(0, context.RequestAborted))
        {
            context.Response.Headers.RetryAfter = "1";
            throw new ManagementError(429, "Two certificate inspections are already running. Try again shortly.");
        }
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), clock);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, deadline.Token);
            CertificateInspectionRequest input;
            try { input = await CertificateInspectionRequest.ReadAsync(context.Request.Body, cancellation.Token); }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            { throw new ManagementError(408, "The certificate upload timed out. Select the file and try again."); }
            using (input)
            return Results.Json(new { data = CertificateInspection.Inspect(input.Pfx, input.Password, input.Hosts, clock) });
        }
        finally { Slots.Release(); }
    }
}
