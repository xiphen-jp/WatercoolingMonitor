# WatercoolingMonitor

Arduino製本格水冷PC用ファン/ポンプコントローラ・使用率データ送信/モニタリングクライアント

*[English](README.md), [日本語](README.ja.md)*

## 概要

Arduino上の [WatercoolingController](https://github.com/xiphen-jp/WatercoolingController) と連携して動作するタスクトレイ常駐型アプリケーションです。

- シリアル通信（USB経由）でデータを送受信します
- 回転数制御に使用されるCPU/GPU使用率を送信します
    - GPU使用率の取得はNVIDIA製GPUのみ対応（nvidia-smi使用）
- 回転数・温度・PWM信号のDuty比などを受信しモニタリング画面に表示します

## ToDo

- 警告表示
- モニタリング画面の動的生成
- 設定画面
- CPU/GPU/マザーボード等からの温度の取得と送信

## 依存関係

- [ReactiveProperty](https://github.com/runceel/ReactiveProperty)

## 作者

[@\_kure\_](https://twitter.com/_kure_)
