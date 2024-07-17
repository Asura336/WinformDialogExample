# WinformDialogExample

从 Unity 端启动控制台进程，从控制台进程调用 WinForm 的会话。相比在 Unity 端使用 Win32 api 启动会话窗体，更方便使用，也有可能使用自定义的窗体。

进程间通信通过命名管道，异步传递 Winform 会话的输出。

## 生成过程

开启工程 `Console Project/FileDialogService/FormConsole/FormConsole.csproj`，发布或者构建 exe，移动 exe 文件到 `Assets\StreamingAssets` 文件夹。

启动 Unity 工程，播放 `Assets\Scenes\Main.unity` 场景。
