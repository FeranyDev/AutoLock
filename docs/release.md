# AutoLock 打包发布流程

本文档描述从源码生成可分发产物的推荐流程。当前项目是 WinUI 3 单项目结构，主要支持两类发布产物：

- 目录版发布包：适合本机测试、压缩分发、排查依赖问题。
- MSIX 旁加载包：适合正式分发给 Windows 11 用户安装。

## 环境要求

- Windows 10 1809 或更高版本，推荐 Windows 11。
- .NET SDK 10.0 或项目当前目标框架要求的 SDK。
- Windows App SDK / Windows SDK 能被 MSBuild 找到。
- 如果要生成签名 MSIX，需要代码签名证书。测试阶段可以使用脚本生成的当前用户自签证书。

## 一键发布脚本

脚本位置：

```powershell
.\scripts\publish.ps1
```

## GitHub Actions

仓库包含两个工作流：

- `.github/workflows/build.yml`：在 `push` 和 `pull_request` 时自动还原、Debug 构建、Release 构建。非 PR 运行会生成 x64 便携目录版和测试证书签名的 MSIX，并分别上传为两个 artifact。
- `.github/workflows/release.yml`：手动触发的发布工作流，可以在 GitHub Actions 页面选择版本号、运行时和发布模式，默认生成全部格式。

手动发布时可选：

- `Folder`
- `Msix`
- `All`

GitHub 下载的 artifact 本身就是 ZIP。工作流会向脚本传入 `-NoZip`，并将便携版上传为 `*-portable`、MSIX 上传为 `*-msix` 两个独立下载项，不会再嵌套一份重复的 `*-folder.zip`。手动发布选择单一模式时，只会上传对应的 artifact。

Actions 会为每次 MSIX 构建创建临时自签代码签名证书，通过当前用户证书库完成签名，然后删除证书库中的私钥。Artifact 只包含公开 `.cer`、MSIX、依赖和安装脚本，不生成或上传 `.pfx`。

解压 `*-msix` artifact，进入 `*_Test` 目录，以管理员身份运行 `Install.ps1`。脚本会提示信任随包提供的测试证书，并安装适用架构的依赖和主 MSIX。测试证书仅适合开发和内部测试；公开发布仍应在仓库 Secrets 中配置正式证书，并让 `release.yml` 调用 `-CertificatePath` / `-CertificatePassword`。

默认生成 x64 Release 目录版发布包：

```powershell
.\scripts\publish.ps1
```

输出目录：

```text
artifacts\release\AutoLock-1.0.0-win-x64\
```

常用参数：

```powershell
.\scripts\publish.ps1 -Configuration Release -RuntimeIdentifier win-x64 -Version 1.0.0 -Clean
```

支持的 RuntimeIdentifier：

- `win-x64`
- `win-x86`
- `win-arm64`

## 目录版发布包

生成目录版并压缩：

```powershell
.\scripts\publish.ps1 -Mode Folder -RuntimeIdentifier win-x64 -Version 1.0.0 -Clean
```

生成后会得到：

```text
artifacts\release\AutoLock-1.0.0-win-x64\folder\
artifacts\release\AutoLock-1.0.0-win-x64\AutoLock-1.0.0-win-x64-folder.zip
```

目录版适合开发测试，但 WinUI 3 应用通常更推荐通过 `dotnet run` 或 MSIX 包运行。直接双击未打包 exe 时，如果目标机器没有对应 Windows App Runtime，可能出现 `REGDB_E_CLASSNOTREG`。

## MSIX 旁加载包

生成未签名 MSIX：

```powershell
.\scripts\publish.ps1 -Mode Msix -RuntimeIdentifier win-x64 -Version 1.0.0 -Clean
```

未签名 MSIX 不能直接安装，主要用于检查打包产物结构。推荐用证书签名。

### 使用测试证书签名

测试阶段可以生成当前用户自签证书，并把公钥安装到当前用户的 TrustedPeople：

```powershell
.\scripts\publish.ps1 `
  -Mode Msix `
  -RuntimeIdentifier win-x64 `
  -Version 1.0.0 `
  -CreateTestCertificate `
  -InstallCertificate `
  -Clean
```

注意：

- `Package.appxmanifest` 当前 Publisher 是 `CN=AppPublisher`。
- 签名证书 Subject 必须与 Publisher 匹配。
- 脚本默认生成 `CN=AppPublisher` 证书，因此无需额外修改。
- 测试证书使用证书库私钥完成签名，输出中只保留公开 CER，不生成 PFX。
- 正式发布时应使用可信代码签名证书，而不是测试证书。

### 使用已有证书

使用 PFX 文件：

```powershell
.\scripts\publish.ps1 `
  -Mode Msix `
  -RuntimeIdentifier win-x64 `
  -Version 1.0.0 `
  -CertificatePath "C:\certs\AutoLock.pfx" `
  -CertificatePassword "pfx-password" `
  -Clean
```

或使用已安装证书的 thumbprint：

```powershell
.\scripts\publish.ps1 `
  -Mode Msix `
  -RuntimeIdentifier win-x64 `
  -Version 1.0.0 `
  -CertificateThumbprint "YOUR_CERT_THUMBPRINT" `
  -Clean
```

## 同时生成目录版和 MSIX

```powershell
.\scripts\publish.ps1 `
  -Mode All `
  -RuntimeIdentifier win-x64 `
  -Version 1.0.0 `
  -CreateTestCertificate `
  -InstallCertificate `
  -Clean
```

## 发布前检查清单

1. 更新版本号：脚本的 `-Version` 会传给 MSBuild，并转换为四段式 MSIX `PackageVersion`。
2. 确认 `Package.appxmanifest` 中的 Publisher 与签名证书 Subject 一致。
3. 执行 Release 构建：

```powershell
dotnet build .\src\AutoLock.WinUI\AutoLock.WinUI.csproj -c Release
```

4. 运行发布脚本生成产物。
5. 在一台干净 Windows 用户环境中安装 MSIX。
6. 验证蓝牙扫描、绑定、自动锁定、后台运行、开机自启、可信 Wi-Fi、电源抑制。
7. 检查 `%LOCALAPPDATA%\AutoLock\winui-crash.log` 没有新增异常。

## 推荐发布产物

正式 release 建议附带：

- 已签名 `.msix` 或 `.msixbundle`
- `AutoLock-版本-win-x64-folder.zip`，用于排查问题
- `README.md`
- 简短 release notes，说明新增功能、修复内容和升级注意事项

不要发布：

- `artifacts\release\...\certificates\*.pfx`
- 自签证书密码
- 本地配置文件、历史记录、崩溃日志
