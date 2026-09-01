using AgentPing.Companion.Core;
using AgentPing.Companion.Windows;

ApplicationConfiguration.Initialize();
Application.Run(new CompanionApplicationContext(showWindow: !StartupLaunchMode.IsBackground(args)));
