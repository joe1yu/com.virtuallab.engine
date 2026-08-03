# 氧气的实验室制取与性质 Sample

该 Sample 提供课程 CSV、预制体、材质、生成资产、演示场景和专项测试，不拥有
通用化学实现。

## 使用方式

1. 在 Unity Package Manager 中先导入“Unity 适配器”Sample。
2. 再导入“化学实验”Sample。
3. 最后导入“氧气的实验室制取与性质”Sample。
4. 执行菜单 `Virtual Lab > 课程 > 编译 > 氧气的实验室制取与性质`。
5. 打开该 Sample 的 `Scenes/OxygenLaboratory.unity` 并运行。

课程 CSV 使用 `课程目录/...` 引用同课程资源，因此导入目录和引擎版本变化后
不需要修改配置表。

如需通过 ArkFramework 和 Addressables 加载资源，再导入“ArkFramework 资源加载”
Sample，并按其 README 同步生成的氧气课程资产即可；课程 CSV 无需修改。
