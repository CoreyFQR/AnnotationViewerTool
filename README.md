# MedVision Annotation Viewer

用于查看图片标注框，支持两种格式：

- LabelMe JSON：例如 `New mycelium in square frame`，同一目录内 `.jpg` + 同名 `.json`。
- YOLO TXT：例如 `small_dataset` / `large_dataset`，图片在 `images`，标注在 `labels`，每行格式为 `class_id x_center y_center width height`。

## 使用

1. 运行 `publish/AnnotationViewer.exe` 或 `publish/AnnotationViewer_yolo.exe`。
2. 如果没有自动打开数据，点击“选择文件夹”，选择包含图片和标注的目录。
3. 左侧选择图片，右侧查看标注框。

默认窗口按 `3072x2048` 这类 3:2 图片做了更宽的右侧画布；左侧文件列表保留为窄栏，主要用于切换图片和查看单图标注数量。

快捷操作：

- 左/右方向键：上一张/下一张。
- `Ctrl` + 鼠标滚轮：缩放。
- `100%`：原始尺寸。
- `适应窗口` 或“自动适应”：按窗口大小显示。

## 支持的数据目录

LabelMe JSON 可以直接选择这种目录：

```text
scanAB[...].jpg
scanAB[...].json
```

YOLO TXT 可以选择以下任一种目录：

```text
small_dataset/
small_dataset/train/
small_dataset/train/images/
```

工具会自动寻找相邻的 `labels` 目录，并读取上级目录里的 `classes.txt`。如果遇到多边形 JSON，也会按点集外接矩形并画出多边形轮廓。

## GT / Pred 对比导出

顶部第二行可以选择 YOLO 预测 `labels` 文件夹，并设置 IoU 阈值。点击“导出对比图”后会生成两个子目录：

- `gt_pred_overlay`：GT 和 Pred 叠加图。GT 为绿色，Pred 为橙色虚线。
- `tp_fp_fn_analysis`：错误分析图。TP 为绿色，FP 为红色，FN 为蓝色虚线。

导出目录位于：

```text
publish/comparison_exports/<timestamp>/
```

Pred labels 需要是 Ultralytics YOLO txt 格式：

```text
class_id x_center y_center width height
class_id x_center y_center width height confidence
```

如果 `runs/predict/...` 下没有 `labels` 子目录，需要重新预测并保存 txt：

```powershell
.\.venv\Scripts\python.exe predict.py --save-txt --save-conf
```

## 重新构建

在 `MedVision` 根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\annotation_viewer_tool\build.ps1
```

构建脚本使用 Windows 自带的 .NET Framework C# 编译器，不需要额外下载依赖。
