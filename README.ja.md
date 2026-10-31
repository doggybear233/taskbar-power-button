<div align="center">

<img src="docs/logo.png" width="88" alt="Taskbar Power Button">

# タスクバーの電源ボタン

**押し間違えても、取り消せるシャットダウン。**

[![release](https://img.shields.io/badge/release-v1.0.0-c8352c)](https://github.com/doggybear233/taskbar-power-button/releases) [![platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078d4)](#動作環境) [![license](https://img.shields.io/badge/license-MIT-3d9970)](LICENSE) [![ui](https://img.shields.io/badge/UI-中文%20%C2%B7%20日本語%20%C2%B7%20English-555555)](#多言語)

[インストール](#インストール) · [使い方](#使い方) · [設定](#設定) · [仕組み](#仕組み) · [既知の制限](#既知の制限)

[English](README.md) · [中文](README.zh-CN.md) · **日本語**

</div>

---

電源ボタンを押すと、タスクバーのすぐ上、スタートボタンの脇に小さな黒いカードが現れます。赤いリングが残り秒数を削っていき、真ん中に数字、隣にこれから何が起きるかが一行で書かれています。もう一度押す——ボタンのどこでも、あるいは `Esc`——カードは消え、何もなかったことになります。

やりたいことはこれだけです。**ワンクリックでシャットダウン、数秒だけ取り消せる。** 確認ダイアログも、「もう一度押して確定」も、スタートメニューまで戻る手間もありません。

カードには 2 つ目のボタンがあり、残りの電源メニュー——スリープ、再起動、サインアウト、ロック、ディスプレイをオフ、時間を指定してシャットダウン——が開きます。角丸も余白もフォントも Windows 11 に合わせて描いているので、どの拡大率でも、どの表示言語でも自然に見えます。

<img src="docs/screenshots/01-taskbar.png" width="133" alt="スタートの隣に並んだ電源ボタン">

| 左クリック | 中止 | メニュー |
| :---: | :---: | :---: |
| <img src="docs/screenshots/02-countdown-ja.png" width="330"> | もう一度ボタンを押す · <kbd>Esc</kbd> · 「シャットダウンを中止」 | <img src="docs/screenshots/04-menu-ja.png" width="150"> |

<sub>同じ画面とメニューの英語・中国語版：</sub>

| English | 中文 |
| :---: | :---: |
| <img src="docs/screenshots/02-countdown-en.png" width="300"><br><img src="docs/screenshots/04-menu-en.png" width="120"> | <img src="docs/screenshots/02-countdown-zh.png" width="300"><br><img src="docs/screenshots/04-menu-zh.png" width="120"> |

- **取り消しは同じボタンで**。窓を探しに行かず、押した場所をもう一度押すだけで済みます。
- **フォーカスを奪いません**。カードは最前面の小さなウィンドウなので、後ろの作業も入力もそのまま続きます。
- **すべて自前で描画**。リングもメニューも余白もフォントもプログラムが描くため、1080p でも 4K でも崩れません。

<sub>裏側は静かです。ログなし、通信なし、常駐プロセスなし、管理者権限も不要。書き込むのはインストール時と設定変更時の 3 か所だけ——タスクバーのショートカット、`%LOCALAPPDATA%\PowerButton\`、`HKCU` のシェル動詞 1 つ。</sub>

## インストール

### 動作環境

Windows 10 / 11（x64）、.NET Framework 4.8（Windows に標準搭載）。.NET SDK も Node もパッケージマネージャーも不要で、インストールに**管理者権限は要りません**。

<details open>
<summary><b>Release から入れる（推奨）</b></summary>

1. [Releases](https://github.com/doggybear233/taskbar-power-button/releases) から `taskbar-power-button-1.0.0.zip` をダウンロードし、任意のフォルダーに展開します。
2. `install.ps1` を右クリック → **PowerShell で実行**。（コマンドなら `powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1`）
3. タスクバーのアイコンがすぐ変わらない場合は、サインアウトして入り直してください。ショートカット自体はすでに書き換わっていて、エクスプローラーのアイコンキャッシュが古いだけです。

`install.ps1` をもう一度実行すればそのままアップグレードになります。元のショートカットのバックアップは初回のみです。
</details>

<details>
<summary><b>ソースから入れる</b></summary>

```powershell
git clone https://github.com/doggybear233/taskbar-power-button.git
cd taskbar-power-button
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

`install.ps1` はアイコンを用意し、.NET Framework 付属の `csc.exe` で `src\PowerButton.cs` をコンパイルし、現在のショートカットをバックアップしてから同じ場所を書き換え、`HKCU` にシェル動詞を登録して、シェルを更新します。
</details>

## 使い方

| 操作 | 動作 |
| --- | --- |
| **左クリック** | カウントダウン画面（既定 5 秒）が開きます。0 になって初めてシャットダウンします |
| **カウントダウン中にもう一度クリック** | 中止します。Windows 側には何も登録していないので、取り消すべき処理は存在しません |
| **<kbd>Esc</kbd> またはカードの「シャットダウンを中止」** | 上と同じ |
| **<kbd>Shift</kbd> + 左クリック** | カウントダウンを始めずに電源メニューを開きます |
| **カード内の「電源メニュー」ボタン** | カウントダウンを待たずに同じメニューを開きます |
| **タスクバーのボタンをダブルクリック** | <kbd>Shift</kbd> + 左クリックと同じ。カウントダウンを飛ばしてメニューを開きます |
| **マウスを重ねる** | ショートカット自身のツールチップ（エクスプローラーが描画） |

メニューの内容は **スリープ · 再起動 · シャットダウン · サインアウト · ロック · ディスプレイをオフ · 時間を指定してシャットダウン… · シャットダウンを中止 · カウントダウン設定…**。再起動も同じ中止できるカウントダウンを通ります。

カードは 200 ミリ秒待ってから表示されます。ダブルクリック（メニューへ直行、シャットダウン画面は一瞬も出ません）を拾える長さで、かつクリックしてから待たされる感じもない長さです。

ツールチップの文言も、カードとメニュー内のすべてのラベルもプログラムがシステム言語で生成するので、日本語・中国語・英語のどの環境でも自然な表記になります。

## 機能

| 機能 | なぜ必要か |
| --- | --- |
| 中止できるカウントダウン | 取り返しのつかない一瞬を、取り消せる遅延に変えます。確認ダイアログより安上がりで、通常のシャットダウンに余計なクリックが要りません |
| もう一度押せば中止 | 押したボタンをもう一度押すだけで取り消せます。画面を探す必要はありません |
| 標準の電源メニュー | スリープ、再起動、サインアウト、ロック、画面オフをスタートメニュー経由なしで実行できます |
| Windows のシャットダウン通知を出さない | Windows が「1 分以内にシャットダウンします」と出すのは、シャットダウンが*予約*されている間だけです。このツールは予約せず、カウントダウンが 0 になってから `ExitWindowsEx` を呼ぶので、システムのダイアログは出ません |
| 強制終了しない | `shutdown.exe /f` も `EWX_FORCE` も使いません。閉じるのを拒否するアプリは従来どおり「これらのアプリがシャットダウンを妨げています」画面に進むので、未保存の作業が消えることはありません |
| 同じ場所・同じアイコン位置 | 既存の `.lnk` をその場で書き換えるので、ピン留めが二重になりません |
| 多言語 | UI、ツールチップ、カードとメニューが Windows の表示言語に追従します |
| 単一ファイル・ランタイム不要 | 約 400 KB の WinForms 実行ファイル。`csc.exe` でビルドでき、インストールも常駐もしません |

## 設定

メニューの **カウントダウン設定…** は `%LOCALAPPDATA%\PowerButton\config.ini` に書き込みます。

```ini
delay=5
lang=auto
```

`delay` は 0〜120 秒（`0` は即実行、非推奨）。`lang` は `auto` / `zh` / `ja` / `en`。

## コマンドライン

```
PowerButton.exe                     左クリック時の動作：カウントダウン後にシャットダウン
PowerButton.exe --menu              電源メニューを直接開く
PowerButton.exe --preview 30        30 秒のカウントダウンを表示するだけ（実行しない）
PowerButton.exe --cancel            メニューの「時間を指定してシャットダウン」を取り消す（shutdown /a）
PowerButton.exe --delay 8           今回だけ 8 秒にする
PowerButton.exe --lang ja|zh|en     今回だけ UI 言語を固定する
PowerButton.exe --selftest          SeShutdownPrivilege を取得できるか確認（0=正常、2=失敗）
PowerButton.exe --set-description <lnk> <テキスト>   ショートカットの説明を書き込む（インストーラー用）
PowerButton.exe --lnk-probe <lnk> <出力>            説明を読み戻して確認する
PowerButton.exe --label-out <キー> <ファイル>        現在の言語で 1 つの文言を書き出す
```

`--preview` は安全に UI を確認する方法です。実際のカウントダウン画面と中止処理を通り、最後にシャットダウンしないだけです。`--selftest` は `ExitWindowsEx` に必要な権限だけを確認し、こちらもシャットダウンしません。

## 仕組み

<img src="docs/diagrams/flow.png" alt="状態遷移：クリック → カウントダウン → 中止または期限到来">

<img src="docs/diagrams/architecture.png" alt="構成：ショートカット、実行ファイル、Win32 呼び出し">

クリックでプロセスが起動し、プロセス自身が秒を数えます。0 になったときだけ `SeShutdownPrivilege` を有効化して `ExitWindowsEx(EWX_SHUTDOWN)` を呼びます。中止はウィンドウを閉じるだけで、Windows には何も登録していないため「何も起きなかった」状態に戻ります。

## ビルド

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize+ `
  /codepage:65001 /win32manifest:src\app.manifest /win32icon:icons\power.ico `
  /out:%LOCALAPPDATA%\PowerButton\PowerButton.exe `
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
  src\PowerButton.cs
```

SDK もプロジェクトファイルも不要です。`csc.exe` は .NET Framework の一部で、Windows 10/11 には必ず入っています。`/codepage:65001` はソースが UTF-8 のため必要です。CI も同じコマンドを実行します（[`.github/workflows/build.yml`](.github/workflows/build.yml)）。

アイコンの再生成：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\make-icon.ps1
```

`icons\power.ico` には 9 サイズ（16 / 20 / 24 / 32 / 40 / 48 / 64 / 128 / 256）が入っており、それぞれを実寸で描画しています。タスクバーは 24〜40 px で表示するため、1 枚の大きな画像から縮小すると細い線が潰れます。ICO の各エントリーは手書きの 32 bpp DIB です。GDI+ とシェルは PNG 圧縮エントリーを受け付けず（空のタイルになります）、`GetHicon()` → `Icon.Save()` の往復ではアルファチャンネルが黙って失われます（黒い四角になります）。

## 多言語

UI は **中文 / 日本語 / English** の 3 言語で、既定では Windows の表示言語（`CultureInfo.CurrentUICulture`）に従います。`ja` → 日本語、`zh` → 中国語、それ以外 → 英語。

優先度の低い順に、次の方法で上書きできます。

```powershell
# %LOCALAPPDATA%\PowerButton\config.ini → lang=zh
$env:POWERBUTTON_LANG = 'zh'
PowerButton.exe --lang zh
```

フォントも言語に合わせて切り替えます（`Microsoft YaHei UI`、`Yu Gothic UI`、`Segoe UI`）。日本語の漢字が中国語の字形で描かれるのを避けるためです。メニューの幅は最も長い項目から測るので、長い日本語ラベルも切れません。言語を変えたあとにツールチップを更新したい場合は、`install.ps1` をもう一度実行してください。

## 既知の制限

- **タスクバーのボタンを右クリックすると開くのはエクスプローラーのメニューで、このアプリのメニューではありません。長押しも反応しません。** タスクバーのボタンはエクスプローラーのもので、ピン留めしたショートカットにできるのは「プログラムを起動する」ことだけです。しかもプログラムはクリックの*後*に起動するため、押下・長押し・右クリックのイベントを受け取れませんし、登録した動詞もピン留め項目には現れません。実現するには常駐プロセスと `WH_MOUSE_LL` のローレベルフック、解像度・DPI・マルチモニターの変化ごとのボタン矩形の再計算が必要です。本プロジェクトは意図的に常駐しません。メニューの導線はダブルクリック、<kbd>Shift</kbd> + 左クリック、カード内の「電源メニュー」ボタンの 3 つです。
- **メニューの「時間を指定してシャットダウン…」だけは今も `shutdown.exe` を使います。** 予約したシャットダウンはこのプロセスの終了後も生きている必要があるためです。残り 60 秒で Windows 自身のダイアログが出ますが、これは予約シャットダウンの仕様です。
- **実行中に DPI が変わるモニターには追従しません。** UI は起動時に取得した DPI で拡大縮小します。異なる拡大率のモニターへ移動すると、再起動するまでサイズが合わないことがあります。
- Windows 11 24H2 / 25H2、150 % 拡大で動作確認しています。Windows 10 でも動くはずですが（使用している API はすべて以前からあります）、実機確認はしていません。

## アンインストール

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1
```

`shutdown.lnk` をインストール時のバックアップ（`shutdown.exe -s -t 0`）に戻し、`HKCU` の動詞と `%LOCALAPPDATA%\PowerButton` を削除します。それ以外には触れていません。

## クレジットとライセンス

- コード：[MIT](LICENSE)。
- アイコン：電源の字形は **Material Symbols** の `power_settings_new`（Google LLC、Apache-2.0）を白い円形バッジに改変し、9 サイズへラスタライズしたものです。[NOTICE](NOTICE) と [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt) を参照してください。
- 選定段階では Material Symbols、Fluent UI System Icons、Lucide（ISC）、Tabler、Phosphor、Bootstrap Icons、Heroicons、Carbon、Ionicons（MIT / Apache-2.0）を比較しましたが、配布するのは Material Symbols の 1 種類だけです。
- Microsoft とは関係ありません。`shutdown.exe`、`ExitWindowsEx`、Windows は Microsoft のもので、本プロジェクトはそれらを呼び出しているだけです。
