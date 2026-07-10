# MedVision Annotation Viewer

用于查看图片标注框，支持 LabelMe JSON 和 YOLO TXT 两种标注格式。

LabelMe JSON 可以直接选择图片和同名 JSON 位于同一层的目录：

```text
dataset/
  image_001.jpg
  image_001.json
  image_002.jpg
  image_002.json
```

YOLO TXT 可以选择数据集根目录、拆分目录，或具体的 `images` 目录：

```text
dataset/
  classes.txt
  train/
    images/
      image_001.jpg
    labels/
      image_001.txt
```

工具会自动寻找相邻的 `labels` 目录，并读取上级目录里的 `classes.txt`。如果遇到多边形 JSON，也会按点集外接矩形并画出多边形轮廓。

## 使用

1. 从 GitHub Releases 下载 `AnnotationViewer.exe`。
2. 双击运行 `AnnotationViewer.exe`。
3. 如果没有自动打开数据，点击“选择文件夹”，选择包含图片和标注的目录。
4. 左侧选择图片，右侧查看标注框。

默认窗口按 `3072x2048` 这类 3:2 图片做了更宽的右侧画布；左侧文件列表保留为窄栏，主要用于切换图片和查看单图标注数量。

快捷操作：

- 左/右方向键：上一张/下一张。
- `Ctrl` + 鼠标滚轮：缩放。
- `100%`：原始尺寸。
- `适应窗口` 或“自动适应”：按窗口大小显示。

## GT / Pred 对比导出

顶部第二行可以选择 YOLO 预测 `labels` 文件夹，并设置 IoU 阈值。点击“导出对比图”后会生成两个子目录：

- `gt_pred_overlay`：GT 和 Pred 叠加图。GT 为绿色，Pred 为橙色虚线。
- `tp_fp_fn_analysis`：错误分析图。TP 为绿色，FP 为红色，FN 为蓝色虚线。

导出目录位于程序所在目录下：

```text
comparison_exports/<timestamp>/
```

Pred labels 需要是 Ultralytics YOLO txt 格式：

```text
class_id x_center y_center width height
class_id x_center y_center width height confidence
```
