using MailStripper.Endpoints;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace MailStripper.Tests;

public class StripEndpointsTests
{
    [Fact]
    public void TryResolveAuthservId_PrimaryFormOrBody_TakesPrecedenceOverHeaderAndQuery()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Expected-Authserv-Id"] = "mx.header.example";
        context.Request.QueryString = new QueryString("?authservId=mx.query.example");

        bool success = StripEndpoints.TryResolveAuthservId("mx.form.example", context.Request, out var id, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("mx.form.example", id);
    }

    [Fact]
    public void TryResolveAuthservId_Header_TakesPrecedenceOverQueryWhenPrimaryIsNull()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Expected-Authserv-Id"] = "mx.header.example";
        context.Request.QueryString = new QueryString("?authservId=mx.query.example");

        bool success = StripEndpoints.TryResolveAuthservId(null, context.Request, out var id, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("mx.header.example", id);
    }

    [Fact]
    public void TryResolveAuthservId_Query_UsedWhenPrimaryAndHeaderAreNull()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?authservId=mx.query.example");

        bool success = StripEndpoints.TryResolveAuthservId(null, context.Request, out var id, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("mx.query.example", id);
    }

    [Fact]
    public void TryResolveAuthservId_TrimsWhitespaceAndSkipsEmpty()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Expected-Authserv-Id"] = "   mx.trimmed.example   ";

        bool success = StripEndpoints.TryResolveAuthservId("   ", context.Request, out var id, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("mx.trimmed.example", id);
    }

    [Fact]
    public void TryResolveAuthservId_Exceeding255Chars_FailsWithValidationError()
    {
        var context = new DefaultHttpContext();
        string oversized = new string('a', 256);

        bool success = StripEndpoints.TryResolveAuthservId(oversized, context.Request, out var id, out var error);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("cannot exceed 255 characters", error);
    }

    [Fact]
    public void TryResolveAuthservId_ContainsControlCharacters_FailsWithValidationError()
    {
        var context = new DefaultHttpContext();
        string malicious = "mx.bad\r\n.example";

        bool success = StripEndpoints.TryResolveAuthservId(malicious, context.Request, out var id, out var error);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("control characters", error);
    }
}
