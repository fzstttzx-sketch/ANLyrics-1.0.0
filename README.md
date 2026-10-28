# ANLyrics

BetterNCM 独立桌面歌词插件。歌词绘制在 BetterNCM 之外的 WPF 分层窗口中，不附带任何 DLL / EXE，启动时由 PowerShell 的 `Add-Type` 在内存中编译 C#。

## 模块划分

| 文件 | 职责 |
| --- | --- |
| `manifest.json` | BetterNCM 插件清单；只注入 JS，不含原生插件。 |
| `src/model.js` | 设置结构、默认值、归一化与颜色工具。 |
| `src/io.js` | 数据目录、JSON 文件读写、字体刷新、Overlay 启动（EncodedCommand）。 |
| `src/lyrics.js` | 歌词获取（liblyric / 公开 API）、LRC 解析、翻译合并、缓存。 |
| `src/player.js` | 播放回调、时间估算、播放/暂停看门狗、歌曲轮询。 |
| `src/app.js` | 歌曲切换、歌词预取、心跳、Overlay 生命周期、桌面关闭同步。 |
| `config.js` / `config.css` | BetterNCM 设置页（字体、颜色、透明度、间距、模式、微调）。 |
| `overlay/Overlay.cs` | 设置/会话/JSON、封面取色、渐变主题、字体目录、文字几何、歌词/命中/解锁/菜单窗口。 |
| `overlay/OverlayController.cs` | 控制器、拖拽、锁定、对齐、菜单动作、文件轮询、存活检查、入口。 |
| `overlay/launch.ps1` | 加载 WPF 程序集、编译 C#、启动 `ANLyrics.OverlayApp`。 |

插件与 Overlay 只通过 `%BETTERNCM_PROFILE%\ANLyrics` 下的小 JSON 文件通信，避免端口和进程参数问题。

## 安装

将 `ANLyrics-1.0.0.plugin` 放入 BetterNCM 的 `plugins` 目录，重启网易云即可。

## 字体

- 设置页的字体下拉读取 `fonts.json`，同时枚举 WPF 与 GDI 字体，包含当前用户目录（`%LOCALAPPDATA%\Microsoft\Windows\Fonts`）里的字体。
- 列表默认 **中文字体在前**，英文和其他字体在后；字体卡片顶部有搜索框，输入中文或英文即可过滤。
- 如果新装的字体没有出现在列表里，打开插件设置，点击字体卡片底部的 **刷新系统字体**；仍不显示时，确认字体是“为当前用户安装”，再重启网易云。
- 本机验证：列表约 374 个字体，包含 `萝莉体 第二版`、`阿里巴巴普惠体 3.0 45 Light`。

## 日志

- `ANLyrics\overlay.log`：Overlay 启动、布局、主题、关闭原因。
- 网易云 `web.log`：插件 JS 的 `console` 输出。

## 已知注意事项

- 如果同时启用了其他桌面歌词插件（如 LyricsIsland），会出现两套桌面歌词；测试或使用时关闭另一个即可。
- 任务栏模式固定左对齐、两行（原文上、翻译下），自动缩放到任务栏高度；任务栏模式下翻译字号大于原文，桌面模式仍按设置分别控制。
- 桌面锁定后歌词区域鼠标穿透；“解锁”平时隐藏，鼠标移到歌词或右侧触发区后才在歌词正上/下方居中显示。
- 解锁与子菜单共用触发区；离开后保留约 650–700ms 缓冲，期间移到子菜单或“解锁”按钮上会保持显示，直到鼠标离开。
