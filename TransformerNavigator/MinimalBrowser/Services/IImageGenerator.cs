namespace MinimalBrowser.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IImageGenerator
    {
        Task<byte[]> GenerateAsync(string prompt, IProgress<double> progress = null, CancellationToken ct = default);
    }
}