# Unity 适配器 Sample

该 Sample 将与 Unity 直接相关的能力放在核心引擎之外，包括场景对象绑定、空间事实、
语义输入、表现执行、课程资源加载，以及中文课程配置的读取、校验和生成工具。

核心引擎不依赖该 Sample；只有需要 Unity 交互与表现的项目才需要导入它。

## 使用方式

1. 在 Unity Package Manager 中选择“虚拟实验引擎”。
2. 在 Samples 页签先导入“Unity 适配器”。
3. 再按需导入“化学实验”或具体课程 Sample。

运行时程序集为 `VirtualLab.UnityAdapters`，课程创作程序集为
`VirtualLab.Unity.Authoring`。资源只通过路径加载接口解析，具体资源类型由表现使用方声明，
后续可在不修改课程模型的情况下接入 Addressables。

课程场景采用一个实验总预制体。运行时只实例化该预制体一次，再按其中
`CourseEntityView` 保存的实体 ID 注册全部操作对象；`实验对象.csv` 不保存逐对象模型预制体引用。
