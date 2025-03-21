# MachineLog - 産業機器ログ分析システム

MachineLogは、約20,000台の産業機器からのログデータを収集・分析するためのプラットフォームです。機器は毎日約100GBのログデータを生成し、1年間のデータ保持が必要です。

## システム要件

- 処理能力: 1秒あたり最大10,000件のログエントリを処理
- 可用性: 99.99%以上（年間ダウンタイム52分以内）
- レイテンシ: ログ収集から保存まで5秒以内
- スケーラビリティ: 最大50,000台の機器まで対応可能

## システム構成

本システムは以下の4つの主要コンポーネントで構成されています：

1. **MachineLog.Collector** - .NET 8.0ベースのログ収集サービス
   - 産業機器からログファイルを監視・収集
   - IoT HubのUploadModuleLogsを使用してBlobストレージにアップロード
   - 効率的なバッチ処理とリトライメカニズムを実装

2. **MachineLog.Monitor** - ASP.NET 8.0 Webアプリケーション
   - Blazor WebAssemblyベースのSPA
   - Azure Storage APIを使用してログデータにアクセス
   - リアルタイム分析と可視化機能を提供

3. **MachineLog.Common** - 共通ライブラリ
   - すべてのコンポーネントで共有されるモデルとユーティリティ
   - ログエントリの検証と処理のための標準化されたロジック

4. **MachineLog.Infrastructure** - インフラストラクチャ定義
   - TerraformによるAzureリソースのプロビジョニング
   - 環境ごとの構成管理（開発、テスト、本番）

## 開発環境のセットアップ

### 前提条件

- .NET 8.0 SDK
- Visual Studio 2022 または Visual Studio Code
- Azure CLI
- Terraform CLI
- Docker Desktop

### ローカル開発環境の構築

1. リポジトリのクローン:
   ```
   git clone https://github.com/your-organization/machinelog.git
   cd machinelog
   ```

2. 依存関係のリストア:
   ```
   dotnet restore
   ```

3. ローカル開発用の設定:
   ```
   cd src/MachineLog.Collector
   dotnet user-secrets init
   dotnet user-secrets set "IoTHub:ConnectionString" "your-connection-string"
   dotnet user-secrets set "Storage:ConnectionString" "your-connection-string"
   ```

4. ビルドと実行:
   ```
   dotnet build
   dotnet run --project src/MachineLog.Collector
   dotnet run --project src/MachineLog.Monitor
   ```

## テスト

### 単体テスト

```
dotnet test
```

### 統合テスト

```
dotnet test tests/MachineLog.IntegrationTests
```

## デプロイ

### インフラストラクチャのデプロイ

```
cd infrastructure/terraform
terraform init -backend-config=environments/dev/dev-backend.conf
terraform plan -var-file=environments/dev/terraform.tfvars
terraform apply -var-file=environments/dev/terraform.tfvars
```

### アプリケーションのデプロイ

```
dotnet publish src/MachineLog.Collector -c Release -o ./publish/collector
dotnet publish src/MachineLog.Monitor -c Release -o ./publish/monitor
```

## プロジェクト構造

```
MachineLog/
├── MachineLog.sln                      # ソリューションファイル
├── src/                                # ソースコードディレクトリ
│   ├── MachineLog.Collector/           # ログ収集サービス
│   ├── MachineLog.Monitor/             # Webアプリケーション
│   │   └── Client/                     # Blazor WebAssemblyクライアント
│   └── MachineLog.Common/              # 共通ライブラリ
├── tests/                              # テストプロジェクト
│   ├── MachineLog.Collector.Tests/     # Collectorのテスト
│   ├── MachineLog.Monitor.Tests/       # Monitorのテスト
│   ├── MachineLog.Common.Tests/        # Commonのテスト
│   └── MachineLog.IntegrationTests/    # 統合テスト
├── docs/                               # ドキュメント
│   ├── architecture/                   # アーキテクチャドキュメント
│   ├── api/                            # API仕様
│   └── guides/                         # 開発・運用ガイド
└── infrastructure/                     # インフラストラクチャ定義
    ├── terraform/                      # Terraformコード
    │   ├── modules/                    # 再利用可能なモジュール
    │   └── environments/               # 環境ごとの設定
    └── scripts/                        # デプロイスクリプトなど
```

## コーディング規約

- Microsoft C#コーディング規約に準拠
- クラス名、メソッド名はPascalCase、変数名はcamelCase
- インターフェースは「I」プレフィックス（例：ILogService）
- プライベートフィールドは「_」プレフィックス（例：_logger）
- 非同期メソッドは「Async」サフィックス
- XMLドキュメンテーションコメントを使用
- nullableリファレンス型を有効化し、nullの可能性を明示
- 使い捨てリソースはIDisposableを実装し適切に破棄

## ライセンス

Copyright © 2025 Your Organization. All rights reserved.