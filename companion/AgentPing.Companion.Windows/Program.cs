using AgentPing.Companion.Core;
using AgentPing.Companion.Windows;

ApplicationConfiguration.Initialize();
using var singleInstance = new Mutex(true, "Local\\AgentPing.Companion", out var ownsInstance);
if (!ownsInstance) return;
Application.Run(new CompanionApplicationContext(showWindow: !StartupLaunchMode.IsBackground(args)));
