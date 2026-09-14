using Core.WSTrade.Authentication;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public class WealthsimpleBrowserBodyTests
{
    // Mirrors the actual WebView2 COM stream: byte-array Read works; modern Stream members throw.
    private sealed class BrowserStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotImplementedException();
        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public override int Read(byte[] buffer, int offset, int count) => offset != 0
            ? throw new NotImplementedException() : inner.Read(buffer, 0, Math.Min(count, 7));
        public override int Read(Span<byte> buffer) => throw new NotImplementedException();
        public override void Flush() => throw new NotImplementedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
        public override void SetLength(long value) => throw new NotImplementedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
    }

    [Fact]
    public void BrowserStreamIsAdaptedWithoutUnsupportedMembers()
    {
        using var source = new BrowserStream(Encoding.UTF8.GetBytes("{\"grant_type\":\"password\"}"));
        using var copy = WealthsimpleBrowserBody.Copy(source);
        using var json = JsonDocument.Parse(copy);
        Assert.Equal("password", json.RootElement.GetProperty("grant_type").GetString());
        Assert.True(source.CanRead); // The event owner retains responsibility for its stream.
    }

    [Theory]
    [InlineData(131072, true)]
    [InlineData(131073, false)]
    public void UnknownLengthBodiesHaveAnEnforcedSizeLimit(int size, bool accepted)
    {
        using var source = new BrowserStream(new byte[size]);
        if (accepted) { using var copy = WealthsimpleBrowserBody.Copy(source); Assert.Equal(size, copy.Length); }
        else Assert.Throws<InvalidDataException>(() => WealthsimpleBrowserBody.Copy(source));
    }
}
