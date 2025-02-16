﻿using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MinecraftLaunch.Launch;
using MinecraftLaunch.Modules.Authenticator;
using MinecraftLaunch.Modules.Enum;
using MinecraftLaunch.Modules.Installer;
using MinecraftLaunch.Modules.Interface;
using MinecraftLaunch.Modules.Models.Auth;
using MinecraftLaunch.Modules.Models.Launch;
using MinecraftLaunch.Modules.Toolkits;
using Natsurainko.Toolkits.Network;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using wonderlab.Class.AppData;
using wonderlab.Class.Utils;
using wonderlab.Class.ViewData;
using wonderlab.control;
using wonderlab.control.Animation;
using wonderlab.Views.Pages;
using wonderlab.Views.Windows;

namespace wonderlab.ViewModels.Pages {
    public class HomePageViewModel : ViewModelBase
    {
        public HomePageViewModel() {
            this.PropertyChanged += OnPropertyChanged;
        }

        public bool Isopen { get; set; } = false;

        public Account CurrentAccount { get; set; } = Account.Default;

        [Reactive]
        public string SelectGameCoreId { get; set; }

        [Reactive]
        public string SearchCondition { get; set; }

        [Reactive]
        public double SearchSuccess { get; set; } = 0;

        [Reactive]
        public double HasGameCore { get; set; } = 0;

        [Reactive]
        public double PanelHeight { get; set; } = 0;

        [Reactive]
        public GameCoreViewData SelectGameCore { get; set; }
        
        [Reactive]
        public ObservableCollection<GameCoreViewData> GameCores { get; set; } = new();
        
        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName is nameof(SearchCondition)) {
                SeachGameCore(SearchCondition);
            }

            if (e.PropertyName is nameof(SelectGameCore) && SelectGameCore != null) {
                GlobalResources.LaunchInfoData.SelectGameCore = SelectGameCore.Data.Id!;
                SelectGameCoreId = SelectGameCore.Data.Id!;
            }
        }

        public async void SeachGameCore(string text) {
            if (!GameCores.Any()) {             
                return;
            }

            GameCores.Clear();
            var result = (await GameCoreUtils.SearchGameCoreAsync(GlobalResources.LaunchInfoData.GameDirectoryPath, text)).Distinct();
            GameCores.Load(result.Select(x => x.CreateViewData<GameCore, GameCoreViewData>()));

            if (!GameCores.Any()) {
                SearchSuccess = 1;
            }
            else SearchSuccess = 0;            
        }

        public async void GetGameCoresAction() {
            GameCores.Clear();
            var cores = await GameCoreUtils.GetLocalGameCores(GlobalResources.LaunchInfoData.GameDirectoryPath);
            HasGameCore = cores.Any() ? 0 : 1;
            GameCores.Load(cores.Select(x => x.CreateViewData<GameCore, GameCoreViewData>()));
        }

        public async void SelectAccountAction() {
            var user = await AccountUtils.GetAsync().ToListAsync();
            DialogPage.ViewModel.GameAccounts = (await AccountUtils.GetAsync().ToListAsync()).ToObservableCollection();

            if (user.Count > 1) {
                App.CurrentWindow.DialogHost.AccountDialog.ShowDialog();
                return;
            } else if (user.Count <= 0) {
                "未添加任何账户，无法继续启动步骤，您可以点击此条以转到账户中心！".ShowMessage("提示", async () => {
                    OpenActionCenterAction();
                    await Task.Delay(1000);
                    new AccountPage().Navigation();
                });
                return;
            }

            CurrentAccount = user.First().Data.ToAccount();
            LaunchTaskAction();
        }

        public async void LaunchTaskAction() {
            $"开始尝试启动游戏 \"{SelectGameCoreId}\"，您可以点击此条进入通知中心以查看启动进度！".ShowMessage(App.CurrentWindow.NotificationCenter.Open);

            NotificationViewData data = new() { 
                Title = $"游戏 {SelectGameCoreId} 的启动任务"
            };

            data.TimerStart();
            NotificationCenterPage.ViewModel.Notifications.Add(data);

            var gameCore = GameCoreToolkit.GetGameCore(GlobalResources.LaunchInfoData.GameDirectoryPath, SelectGameCoreId);
            if (!Path.Combine(JsonUtils.DataPath, "authlib-injector.jar").IsFile()) {
                await DownloadAuthlibAsync();
            }

            //Modpack 重复处理
            int modCount = 0;
            if (gameCore.GetModsPath().IsDirectory()) {
                await ModpackCheckAsync();
            }

            //异步刷新游戏账户
            try {
                await AccountRefreshAsync();
            }
            catch (Exception ex) {
                $"账户刷新失败，详细信息：{ex}".ShowInfoDialog("程序遭遇了异常");
                data.TimerStop();
                return;
            }

            //游戏依赖检查
            await ResourceCheckOutAsync();

            data.Progress = "开始启动步骤 - 0%";
            var javaInfo = GetCurrentJava();
            var config = new LaunchConfig() {
                JvmConfig = new() {
                    AdvancedArguments = new List<string>() { GlobalResources.LaunchInfoData.JvmArgument, GetJvmArguments() },
                    MaxMemory = GlobalResources.LaunchInfoData.IsAutoGetMemory 
                    ? GameCoreUtils.GetOptimumMemory(!gameCore.HasModLoader,modCount).ToInt32() 
                    : GlobalResources.LaunchInfoData.MaxMemory,
                    JavaPath = SystemUtils.IsWindows ? javaInfo.JavaPath.ToJavaw().ToFile() : javaInfo.JavaPath.ToFile(),
                },
                GameWindowConfig = new() {
                    Width = GlobalResources.LaunchInfoData.WindowWidth,
                    Height = GlobalResources.LaunchInfoData.WindowHeight,
                    IsFullscreen = GlobalResources.LaunchInfoData.WindowHeight == 0 && GlobalResources.LaunchInfoData.WindowWidth == 0,
                },
                Account = CurrentAccount,
                WorkingFolder = gameCore.GetGameCorePath().ToDirectory()!,
            };

            JavaMinecraftLauncher launcher = new(config, GlobalResources.LaunchInfoData.GameDirectoryPath, true);
            using var gameProcess = await launcher.LaunchTaskAsync(GlobalResources.LaunchInfoData.SelectGameCore, x => { 
                Trace.WriteLine($"[信息] {x.Item2}");
                data.Progress = $"{x.Item2} - {Math.Round(x.Item1 * 100, 2)}%";
                data.ProgressOfBar = Math.Round(x.Item1 * 100, 2);
            });

            data.ProgressOfBar = 100;
            if (gameProcess.State is LaunchState.Succeess) {
                data.Progress = $"启动成功 - 100%";
                $"游戏 \"{GlobalResources.LaunchInfoData.SelectGameCore}\" 已启动成功，总用时 {data.RunTime}".ShowMessage("启动成功");
                var viewData = gameProcess.CreateViewData<MinecraftLaunchResponse, MinecraftProcessViewData>(CurrentAccount, javaInfo);

                ProcessManager.GameCoreProcesses.Add(viewData);
                gameProcess.ProcessOutput += ProcessOutput;

                gameProcess.Process.Exited += (sender, e) => {
                    Trace.WriteLine($"[信息] 游戏退出");

                    ProcessManager.GameCoreProcesses.Remove(viewData);
                    ProcessManager.History.Add(new(viewData.Data.Process.ExitTime.ToString("T"), viewData.Data.GameCore.Id!));
                };

                await gameProcess.WaitForExitAsync();
            }
            else {
                data.Progress = $"启动失败 - 100%";
                $"游戏 \"{GlobalResources.LaunchInfoData.SelectGameCore}\" 启动失败，详细信息 {gameProcess.Exception}".ShowInfoDialog("程序遭遇了异常");
            }
            data.TimerStop();

            async ValueTask DownloadAuthlibAsync() {
                data.Progress = "下载 Authlib-Injector 中";
                var result = await HttpWrapper.HttpDownloadAsync("https://download.mcbbs.net/mirrors/authlib-injector/artifact/45/authlib-injector-1.1.45.jar",
                    JsonUtils.DataPath, "authlib-injector.jar");
                Trace.WriteLine($"[信息] Http状态码为 {result.HttpStatusCode}");
            }

            async ValueTask ModpackCheckAsync() {
                data.Progress = "开始检查 Mod";
                ModPackToolkit toolkit = new(gameCore, true);
                var modpacks = (await toolkit.LoadAllAsync()).Where(x => x.IsEnabled);
                modCount = modpacks.Count();

                var result = modpacks.GroupBy(i => i.Id).Where(g => g.Count() > 1);
                if (result.Count() > 0) {
                    foreach (var item in result) {
                        $"模组 \"{item.ToList().First().FileName}\" 在此文件夹已有另一版本，可能导致游戏无法正常启动，已中止启动操作！".ShowMessage();
                        return;
                    }
                }
            }

            async ValueTask AccountRefreshAsync() {
                data.Progress = "开始刷新账户";

                await Task.Run(async () => {
                    if (CurrentAccount.Type == AccountType.Yggdrasil) {
                        YggdrasilAuthenticator authenticator = new YggdrasilAuthenticator((CurrentAccount as YggdrasilAccount)!.YggdrasilServerUrl, "", "");
                        var result = await authenticator.RefreshAsync((CurrentAccount as YggdrasilAccount)!);

                        CurrentAccount = result;
                        await AccountUtils.RefreshAsync((await AccountUtils.GetAsync().ToListAsync()).Where(x => x.Data.Uuid == result.Uuid.ToString()).First().Data, result);
                    } else if (CurrentAccount.Type == AccountType.Microsoft) {
                        MicrosoftAuthenticator authenticator = new(AuthType.Refresh) {
                            ClientId = GlobalResources.ClientId,
                            RefreshToken = (CurrentAccount as MicrosoftAccount)!.RefreshToken!
                        };

                        var result = await authenticator.AuthAsync(x => data.Progress = $"当前步骤：{x}");
                        CurrentAccount = result;
                        await AccountUtils.RefreshAsync((await AccountUtils.GetAsync().ToListAsync()).Where(x => x.Data.Uuid == result.Uuid.ToString()).First().Data, result);
                    }
                });
            }

            async ValueTask ResourceCheckOutAsync() {
                data.Progress = "开始检查游戏依赖 - 0%";

                await Task.Run(async() => {
                    ResourceInstaller installer = new(gameCore!);
                    await installer.DownloadAsync(async (s, f) => {
                        await Task.Run(async () => {
                            try {
                                data.Progress = $"补全文件中：{s}";
                                data.ProgressOfBar = f * 100;
                                await Task.Delay(1000);
                            }
                            catch {
                                "Error".ShowLog();
                            }
                            finally {
                                GC.Collect();
                            }
                        });
                    });
                });
            }
        }

        public async void ImportModpacksAction() {
            var result = await DialogUtils.OpenFilePickerAsync(new List<FilePickerFileType>() {
                new("整合包文件") { Patterns = new List<string>() { "*.zip", "*.mrpack" } }
            }, "请选择整合包文件");

            if(result!.IsNull()) {
                return;
            }

            await ModpacksUtils.ModpacksInstallAsync(result.FullName);
        }

        public void OpenActionCenterAction() {
            var back = App.CurrentWindow.Back;
            OpacityChangeAnimation opacity = new(false) {
                RunValue = 0,
            };

            opacity.RunAnimation(back);
            App.CurrentWindow.CloseTopBar();
            new ActionCenterPage().Navigation();
        }

        public void OpenConsoleAction() {
            if(!ConsoleWindow.IsOpen) {
                new ConsoleWindow().Show();
            }
            else {
                ConsoleWindow.WindowActivate();
            }
        }

        public JavaInfo GetCurrentJava() {
            if (GlobalResources.LaunchInfoData.IsAutoSelectJava) {
                var first = GlobalResources.LaunchInfoData.JavaRuntimes.Where(x => x.Is64Bit && 
                x.JavaSlugVersion == new GameCoreToolkit(GlobalResources.LaunchInfoData.GameDirectoryPath)
                .GetGameCore(GlobalResources.LaunchInfoData.SelectGameCore).JavaVersion);                

                if (first.Any()) {
                    return first.First();  
                } else {
                    var second = GlobalResources.LaunchInfoData.JavaRuntimes.Where(x => x.JavaSlugVersion == new GameCoreToolkit(GlobalResources.LaunchInfoData.GameDirectoryPath)
                   .GetGameCore(GlobalResources.LaunchInfoData.SelectGameCore).JavaVersion);
                    
                    return second.Any() ? second.First() : GlobalResources.LaunchInfoData.JavaRuntimePath;
                }
            }

            return GlobalResources.LaunchInfoData.JavaRuntimePath;
        }

        public string GetJvmArguments() {
            if (CurrentAccount.Type == AccountType.Yggdrasil) { 
                var account = CurrentAccount as YggdrasilAccount;
                return $"-javaagent:{Path.Combine(JsonUtils.DataPath, "authlib-injector.jar")}={account!.YggdrasilServerUrl}";
            }

            return string.Empty;
        }

        private void ProcessOutput(object? sender, IProcessOutput e) {
            Trace.WriteLine(e.Raw);
        }
    }
}
