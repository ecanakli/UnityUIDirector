using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>Stored settings; the sample's implementation is <c>SettingsStore</c> in Support.</summary>
    public interface ISettingsStore
    {
        void ResetToDefaults();
        UniTask SaveAsync(CancellationToken ct);
    }

    /// <summary>The items the library lists; the sample's implementation is <c>ItemLibrary</c> in Support.</summary>
    public interface IItemLibrary
    {
        int Count { get; }
        event Action<string> Renamed;
        string GetTitle(string itemId);
        bool CanRename(string itemId);
        void Rename(string itemId, string title);
    }

    /// <summary>The services views receive from the bootstrap.</summary>
    public sealed class AppServices
    {
        public ISettingsStore Settings;
        public IItemLibrary Library;
    }

    /// <summary>A view that needs services implements this; the root's inject seam calls it on every new instance.</summary>
    public interface IServiceReceiver
    {
        void Receive(AppServices services);
    }
}
