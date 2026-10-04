using Golemancer.Runtime;
namespace Golemancer.Desktop;
// Compatibility adapter for the Windows host and its existing verification flows.
internal sealed class DesktopSession(string root, CookedGame content) : Golemancer.Client.GameSession(root, content);
