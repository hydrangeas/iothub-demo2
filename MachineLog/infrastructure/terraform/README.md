# MachineLog インフラストラクチャ（Terraform）

このディレクトリには、MachineLogアプリケーションのAzureインフラストラクチャをTerraformを使用してデプロイするためのコードが含まれています。

## 概要

このTerraformコードは、MachineLogアプリケーションに必要な以下のAzureリソースをプロビジョニングします：

- リソースグループ
- Azure Monitor（Log Analytics、Application Insights）
- ネットワーク（Virtual Network、サブネット）
- ストレージアカウント
- IoT Hub
- Cosmos DB
- App Service
- Function App
- Entra ID（Azure AD）アプリケーション登録

## ディレクトリ構造

```
terraform/
├── .terraform/              # Terraformキャッシュディレクトリ（gitignore対象）
├── .terraform.lock.hcl      # 依存関係ロックファイル
├── backend.conf.example     # Azureバックエンド設定例
├── environments/            # 環境別設定ディレクトリ
├── main.tf                  # メインのTerraform設定ファイル
├── modules/                 # 再利用可能なTerraformモジュール
├── outputs.tf               # 出力変数定義
├── terraform.tfvars.example # 変数値の設定例
└── variables.tf             # 入力変数定義
```

## 使用方法

1. `terraform.tfvars.example`を`terraform.tfvars`にコピーし、必要な値を設定します
2. `backend.conf.example`を`backend.conf`にコピーし、状態ファイル保存先を設定します
3. 以下のコマンドを実行してデプロイします：

```bash
# 初期化
terraform init -backend-config=backend.conf

# 計画の確認
terraform plan -var-file=terraform.tfvars

# デプロイ実行
terraform apply -var-file=terraform.tfvars
```

## 環境別デプロイ

異なる環境（開発、テスト、本番）向けに異なる設定を使用する場合は、`environments`ディレクトリ内の対応するサブディレクトリにある設定ファイルを使用します。

## モジュール

`modules`ディレクトリには、以下の再利用可能なTerraformモジュールが含まれています：

- `azure-monitor` - Log AnalyticsとApplication Insightsの設定
- `networking` - Virtual NetworkとSubnetの設定
- `azure-storage` - ストレージアカウントの設定
- `iot-hub` - IoT Hubの設定
- `cosmos-db` - Cosmos DBの設定
- `app-service` - App Serviceの設定
- `function-app` - Function Appの設定
- `entra-id` - Entra ID（Azure AD）アプリケーション登録の設定

## 注意事項

- 本番環境へのデプロイ前に、必ず`terraform plan`で変更内容を確認してください
- 機密情報（クライアントシークレットなど）は環境変数または安全な方法で管理してください
- 状態ファイルには機密情報が含まれる可能性があるため、適切にアクセス制御されたバックエンドを使用してください