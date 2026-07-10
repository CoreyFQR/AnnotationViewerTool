# MedVision Annotation Viewer

用于查看图片标注框，支持 LabelMe JSON 和 YOLO TXT 两种标注格式，并提供标注统计、GT/Pred 对比、错误分析、误差统计和对比图导出。

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

工具会自动寻找相邻的 `labels` 目录，并读取上级目录里的 `classes.txt`。如果遇到多边形 JSON，也会按点集外接矩形显示，并画出多边形轮廓。

## 使用

1. 从 GitHub Releases 下载 `AnnotationViewer.exe`。
2. 双击运行 `AnnotationViewer.exe`。
3. 如果没有自动打开数据，点击“选择文件夹”，选择包含图片和标注的目录。
4. 左侧选择图片，右侧查看标注框。
5. 如果需要对比模型预测结果，再在 `Pred labels` 中选择 YOLO 预测 `labels` 文件夹。

## 功能

- 标注浏览：左侧图片列表用于切换图片，右侧显示图片、标注框和标注列表；支持显示/隐藏标签、缩放、适应窗口和刷新当前目录。未选择 `Pred labels` 时，普通标注框按多色样式显示，方便区分不同框。
- 标注统计：点击 `统计标注` 后，可按文件夹和类别统计图片数、标注文件数、标注格式和标注框数；统计表支持复制和导出 CSV。
- GT / Pred 对比：`Pred labels` 默认留空；只有选择有效的 YOLO 预测 `labels` 文件夹后，才会进入对比模式，并启用 IoU、`仅显示GT`、`错误分析`、`统计误差` 和 `导出对比图`。
- 显示模式：对比模式下支持 `仅显示GT`，也支持开启 `错误分析`，用 TP、FP、FN 区分正确预测、误检和漏检。清空 `Pred labels` 后会回到普通多色标注查看模式。
- 误差统计：点击 `统计误差` 后，可按文件夹和类别统计 GT 框数、Pred 框数、TP、FP、FN、Precision、Recall 和 F1；统计表支持复制和导出 CSV。
- 对比图导出：点击 `导出对比图` 后，会导出 GT/Pred 叠加图和 TP/FP/FN 错误分析图。

## GT / Pred 格式与导出

`Pred labels` 为空或无效时，GT/Pred 对比相关控件会被禁用，工具只作为普通标注浏览器使用。

点击 `导出对比图` 后会生成两个子目录：

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
