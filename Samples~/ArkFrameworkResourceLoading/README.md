# ArkFramework 资源加载 Sample

该 Sample 使用 ArkFramework 的 `IResourceService` 和 Addressables 后端加载课程资源，
同时保持虚拟实验核心、课程配置及 Unity 适配器不依赖 ArkFramework。

运行时会在课程场景装配前异步预加载全部资源，并持有 ArkFramework 资源 lease；
场景装配和表现执行继续读取同步缓存，不会在 Unity 主线程阻塞等待 Addressables。
组件销毁时会统一释放 lease。

## 安装与导入

1. 在 Package Manager 中通过 Git URL 安装 ArkFramework：
   `https://github.com/joe1yu/com.kmax.arkframework.git#main`。
2. 从“虚拟实验引擎”依次导入“Unity 适配器”和“ArkFramework 资源加载”。
3. 在场景中配置 ArkFramework `FrameworkHost`，并确保其 `FrameworkProfile`
   包含 `ResourceModuleInstaller`。
4. 在 `ConfigDrivenCourseBootstrap` 所在对象添加
   `ArkFrameworkCourseResourceLoader`；组件也会自动补齐课程启动器。

## 同步课程资源

在 Project 窗口选择生成的 `CompiledCourseAsset`，执行：

`Virtual Lab > 课程 > 资源 > 同步选中课程到 ArkFramework Addressables`

工具会在“虚拟实验课程”Addressables Group 中注册课程资源。Addressables 地址与
原 Resources 运行时路径保持一致，因此不需要修改中文课程 CSV。之后按项目发布方式
配置 Addressables Play Mode Script 并构建内容即可。

如宿主项目已有自己的地址规则，可以在课程初始化前调用
`ConfigureAddressResolver`；如不使用全局 `FrameworkHost`，也可以通过
`ConfigureResourceService` 显式注入 `IResourceService`。
