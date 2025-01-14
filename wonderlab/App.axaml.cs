using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using wonderlab.Class.Models;
using wonderlab.Class.Utils;
using wonderlab.control;
using wonderlab.control.Controls.Bar;
using wonderlab.Views.Windows;
using MainWindow = wonderlab.Views.Windows.MainWindow;

namespace wonderlab {
    public partial class App : Application {
        public static MainWindow CurrentWindow { get; protected set; } = null!;

        public override void Initialize() {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException; ;
            AvaloniaXamlLoader.Load(this);
        }

        private async void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) {
            await new WizardWindow().ShowDialog(CurrentWindow);
            StringBuilder builder = new();
            builder.AppendLine("非常抱歉您的 WonderLab 又又又炸了，以下是此次崩溃的错误信息");
            builder.AppendLine("----------------------------------------------------------------------");
            builder.AppendLine($"系统平台：{SystemUtils.GetPlatformName()}");
            builder.AppendLine($"异常名：{(e.ExceptionObject as Exception)!.GetType().FullName}");
            builder.AppendLine("----------------------------------------------------------------------");
            builder.AppendLine($"异常堆栈信息：{(e.ExceptionObject as Exception)}");

            await File.WriteAllTextAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"小蓝瓶错误报告-{(e.ExceptionObject as Exception)!.GetType().FullName}.txt"),builder.ToString());
            JsonUtils.WriteLaunchInfoJson();
            JsonUtils.WriteLauncherInfoJson();
        }

        public override void OnFrameworkInitializationCompleted() {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                desktop.MainWindow = CurrentWindow = new MainWindow();
                Manager.Current = CurrentWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}