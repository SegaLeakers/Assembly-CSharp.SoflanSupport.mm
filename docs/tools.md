# 离线工具与验证

仓库中的离线程序不进入游戏 patch。它们分别用于数值诊断、一次性格式转换、转换结果核验和自动回归，支持范围并不相同。

## 工具总表

| 工具 | 目标框架 | 用途 | 是否进入 `.mm.dll` |
| --- | --- | --- | --- |
| `SoflanCalculator` | `net472` | 按 MA2 物理行号复现 Tap 式 Soflan 视觉数值 | 否 |
| `tools/Convert-Ma2ToMajdata.ps1` | PowerShell | 把受限 MA2 BPM/SFL/lane note 转为 Majdata 文本 | 否 |
| `tools/MajdataValidation` | `net8.0` | 用 MajSimai 重新解析转换结果并比较关键集合 | 否 |
| `tools/SoflanMarkerTests` | `net8.0` | 验证共享 marker 语法 | 否 |
| `tools/SoflanLogTests` | `net8.0` | 验证 Release DIAG 开关、异步 ERROR 日志和 UTF-8 无 BOM | 否 |
| `tools/SoflanMaiBugTests` | `net472` | 验证时间轴、MaiBug、可见性、多 group 和双玩家数值 | 否 |
| `tools/SoflanPrecisionTests` | `net472;net8.0` | 验证 `TimeSpan`、`SoflanPosition` 和大绝对位置下的局部精度 | 否 |
| `tools/SoflanVisibilityTests` | `net472;net8.0` | 对比 TimeSpan/TotalGrid 可见范围、首次注册帧、峰值与稳态分配 | 否 |
| `tools/SoflanRuntimeIntegrationTests` | `net472` | 用原版 `NotesReader` 和真实 MA2 验证 TGrid/BPM/SFL 集成 | 否 |
| `tools/SoflanClockTests` | `net472` | 验证 patch 后 `SoflanGameClock` 重链接 | 否 |
| `tools/SoflanPatchHarness` | `net472` | 应用 MonoMod patch 并做 IL/Cecil fail-fast 检查 | 否 |

## SoflanCalculator

运行格式：

```powershell
dotnet run --project .\SoflanCalculator\SoflanCalculator.csproj -c Release -- `
  <ma2文件> <note物理行号> <运行时当前毫秒> [GetAdjustMSec]
```

示例：

```powershell
dotnet run --project .\SoflanCalculator\SoflanCalculator.csproj -c Release -- `
  'D:\yourCharts\example.ma2' 42 2560 60
```

参数中的行号是 MA2 文件的 1-based 物理行号，不是第几个 note。首次计算后进入交互循环：

```text
line=84
time=3200
speed=750
offset=60
ma2=D:\yourCharts\other.ma2
line=84 time=3200 speed=750 offset=60
q
```

输出包括玩家物件速度、`DefaultTime`、MaiBug、运行时与 MA2 原始时间、当前 group 倍率、`diffPosition`、Guide alpha、物件缩放和 Y 坐标。默认画面常量为 `StartPos=120`、`EndPos=400`，默认玩家物件速度值为 `600`。

适用边界：

- parser 能识别旧/新 Tap、Hold、Star、Touch 和 Slide tag，但计算引擎只复现 `NoteBase` 的 Tap 式视觉公式。
- 不复现 Hold body、Touch 聚拢动画、Slide 路径、Star 旋转或 Break 特效。
- 共享 marker parser 只用于取得 group；`Fspeed` 会被忽略，不模拟 FixedSoflan。
- 命令行固定以 `enableMaiBugAdjust=true` 计算，当前没有关闭开关。
- 非法 SFL 会被 parser 跳过；非法 marker 会回退 group `0`。本工具不是语法校验器。
- `StartPos` / `EndPos` 是典型 prefab 值；目标资源不同会造成坐标差异。

`SoflanCalculator/test_sfl.ma2`、`test_nosfl.ma2`、`test_nogroup.ma2` 和 `test_bpm.ma2` 可用于快速手工试算。

## Convert-Ma2ToMajdata.ps1

始终显式传入输入路径，避免使用脚本中为开发样本保留的默认绝对路径：

```powershell
& .\tools\Convert-Ma2ToMajdata.ps1 `
  -InputPath 'D:\yourCharts\example_02.ma2' `
  -OutputPath 'D:\yourCharts\example_02.majdata.txt' `
  -MusicXmlPath 'D:\yourCharts\Music.xml' `
  -InoteIndex 3
```

参数默认规则：

| 参数 | 省略时行为 |
| --- | --- |
| `OutputPath` | 在输入旁生成 `<原文件名>.majdata.txt` |
| `MusicXmlPath` | 自动尝试输入目录中的 `Music.xml` |
| `InoteIndex` | 文件名匹配 `_NN.ma2` 时使用 `NN+1`，否则为 `1` |

脚本读取 `RESOLUTION`、`BPM`、`SFL` 以及以下 note tag：

```text
NMTAP NMSTR EXTAP EXSTR BRTAP BRSTR BXTAP BXSTR NMTTP
```

它把 BPM 写为 `(bpm)`，把 SFL 起点写为 `<HSgroup*speed>`，把非 0 group note 包在 `<HSgroup>(...)` 中。一个非 `1.0x` duration SFL 结束后，若同组没有立即开始或重叠的下一条 SFL，脚本会补一个 `1x` reset 事件。

输出为 UTF-8 without BOM，并额外生成：

```text
<OutputPath>.summary.json
```

summary 记录元数据、resolution、BPM/SFL/note 数量、自动补的 reset 数量和最大 grid。

该脚本是受限的调查/迁移工具，不是无损 MA2 转换器：

- group 只从第 5 个分词字段中形如 `#<非负整数>` 的完整值读取。
- 不识别 marker 位于其它字段、混合 `!m` / `!y`、有符号 group 或 `#groupFspeed`；Fixed 信息不会保留。
- Star/Break/EX/BX 只映射相应 Majdata 后缀；`NMTTP` 也进入 lane note 文本路径，不保证保留 Touch 语义。
- Hold、TouchHold、Slide、ConnectSlide 等匹配已知 note 前缀的记录会使整个转换失败，并预览前 10 条不支持记录。
- 不在 switch 范围内的其它 MA2 元数据通常被忽略。
- 同一 grid、同一 group 的多个 speed 声明会由后写入值覆盖。

生产谱面转换前必须检查输出并运行验证器或编辑器解析。

## MajdataValidation

该项目通过绝对 `ProjectReference` 使用：

```text
F:\yourMajdataEdit\MajSimaiX\MajSimai.csproj
```

`F:\yourMajdataEdit` 是文档中的脱敏占位符。该工具项目当前使用本机绝对 `ProjectReference`；运行前请在自己的副本中把它改为实际的 `MajSimai.csproj` 路径，并显式提供参数：

```powershell
dotnet run --project .\tools\MajdataValidation\MajdataValidation.csproj -c Release -- `
  'D:\yourCharts\example_02.ma2' `
  'D:\yourCharts\example_02.majdata.txt' `
  3
```

第三个参数是 1-based `inote` 索引，省略时为 `3`。程序用 MajSimai 解析输出，并以 JSON 报告比较：

- BPM：`grid + bpm` 多重集合必须完全相等。
- note：`NMTAP`、`NMSTR`、`EXTAP`、`EXSTR`、`BRTAP`、`BRSTR`、`BXTAP`、`BXSTR` 按 `grid + group + position` 多重集合比较。
- SFL：原 MA2 的 `grid + group + speed` 声明必须全部出现在解析结果中；允许转换器补出的额外 reset。

任一必需比较失败时进程退出码为 `1`。

验证器不会证明完整语义等价：它不读取源 MA2 的 `NMTTP`，也不比较 SFL duration、Fixed speed、Break/EX/Star 属性、Touch 类型、metadata、逗号间细分显示或未支持 note。它适合核对该转换器关注的 BPM、SFL 起点和 lane note 时序，不替代编辑器与游戏内验收。

## 自动测试

### Marker

```powershell
dotnet run --project .\tools\SoflanMarkerTests\SoflanMarkerTests.csproj -c Release
```

覆盖默认/显式 group、`!` 混合修饰、大小写 `F`、有符号 group、Fixed speed，以及空 marker、多个 marker、非法数值和非正 speed。

### 日志

```powershell
dotnet run --project .\tools\SoflanLogTests\SoflanLogTests.csproj -c Release
```

在临时目录触发启用/禁用的异步 DIAG 和 ERROR，最多等待 5 秒，验证 Release 下 DIAG 会写入、关闭 `EnableSoflanDiagnosticLog` 后不写、ERROR 不受影响、UTF-8 严格可解码且无 BOM。

### MaiBug 与运行时时间轴

```powershell
dotnet run --project .\tools\SoflanMaiBugTests\SoflanMaiBugTests.csproj -c Release
```

无参数测试覆盖：

- MaiBug 纯公式、开关和负时间钳制。
- 非零 `GetAdjustMSec()` 还原及缺失偏移的回归证据。
- 恒定加速/减速、停车、反向和跨 SFL 边界。
- group 独立、可见范围、复杂轨迹平移不变性。
- `100001x` 等亚帧视觉窗口被跳过时，原版注册窗口仍能接管 note 生命周期。
- 两个玩家使用不同运行时 chart offset 时的隔离。

可选真实谱面参数顺序是“复杂 SFL 谱面、无 SFL 或恒定 1x 基线谱面”：

```powershell
dotnet run --project .\tools\SoflanMaiBugTests\SoflanMaiBugTests.csproj -c Release -- `
  'F:\yourGame\Package\option\yourOption\music\yourMusic\example_04.ma2' `
  'F:\yourGame\Package\option\yourOption\music\yourMusic\example_01.ma2'
```

只传第一个参数时执行真实复杂谱面的时间平移不变性；同时传第二个参数时再做基线视觉等价比较。

### TotalGrid 可见性

```powershell
dotnet run --project .\tools\SoflanVisibilityTests\SoflanVisibilityTests.csproj -c Release -f net8.0 -- coreclr
```

该 harness 同时调用 `FillVisibleTimeSpanRangesForGamePreview()` 和
`FillVisibleTotalGridRangesForGamePreview()`，覆盖 `1x`、`2x`、`0.5x`、停车、负速、
折返、多 BPM、多 group 和尾段。每帧比较范围端点前后一个 grid、规则采样和固定种子随机点，
并要求首次注册 frame 与模拟峰值可见 note 数完全一致。预热后还验证 output/scratch 实例与容量稳定；
CoreCLR 使用 `GC.GetAllocatedBytesForCurrentThread()` 断言稳态分配为零，Unity Mono 输出明确的
profiler-only 标记。

### Mono-first 完整 runner

```bash
SOFLAN_PATCHED_ASSEMBLY=/tmp/Assembly-CSharp.SoflanSupport.dll \
SOFLAN_INTEGRATION_CHART=/path/to/chart.ma2 \
tools/run-soflan-tests.sh
```

runner 优先查找 Unity Editor 自带 Mono 和 `4.7.2-api`，依次执行 precision、TotalGrid
visibility、MaiBug、真实谱面 integration 和 clock。TotalGrid visibility 还会附加运行一次
CoreCLR `net8.0`，用于线程分配断言。找不到 Unity Mono 时只执行可用的 CoreCLR precision/
visibility，并明确标记 Mono-only 项为 skipped。

## 验证层级

自动测试只验证共享 parser 和纯数值模型。完整发布验收仍应依次包含：

1. Release / Debug 主 patch 构建。
2. precision、visibility、MaiBug、runtime integration、clock 和 patch harness 全部通过。
3. MonoMod 实际应用成功且启动输出没有 Rules 锚点失败。
4. 游戏内验证无 SFL、1x、停车、负速、多 group、Hold、Touch、FixedSoflan 和 P1/P2。
5. 对谱面转换任务，再执行 MajSimai 解析、编辑器检查和目标游戏实际播放。
