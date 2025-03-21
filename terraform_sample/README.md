# MachineLog.Infrastructure - Terraformによるインフラストラクチャ管理

このディレクトリには、MachineLogシステムのAzureインフラストラクチャをコードとして管理するためのTerraformファイルが含まれています。

## 目次

1. [概要](#概要)
2. [前提条件](#前提条件)
3. [ディレクトリ構造](#ディレクトリ構造)
4. [環境変数の設定](#環境変数の設定)
5. [バックエンド設定](#バックエンド設定)
6. [Terraformの実行方法](#terraformの実行方法)
7. [各環境での実行](#各環境での実行)
8. [よくある問題とトラブルシューティング](#よくある問題とトラブルシューティング)

## 概要

MachineLog.Infrastructureは、Terraformを使用してAzureリソースをコードとして管理するプロジェクトです。主要なコンポーネントとして、Log Analyticsワークスペース、ストレージアカウント、App Service、Microsoft Entra IDの設定が含まれています。

## 前提条件

Terraformを実行するには、以下のツールとアクセス権が必要です：

1. **Terraformのインストール** (バージョン1.0.0以上)
   ```
   choco install terraform
   ```
   または[公式サイト](https://www.terraform.io/downloads.html)からダウンロード

2. **Azure CLIのインストール**
   ```
   choco install azure-cli
   ```
   または[公式サイト](https://docs.microsoft.com/ja-jp/cli/azure/install-azure-cli)からダウンロード

3. **Azureアカウントへのアクセス権**
   - サブスクリプション管理者権限
   - または、リソースグループへのContributor権限

4. **Azure CLIでのログイン**
   ```
   az login
   ```

5. **サブスクリプションの選択**（複数のサブスクリプションがある場合）
   ```
   az account set --subscription "サブスクリプションID"
   ```

## ディレクトリ構造

```
MachineLog.Infrastructure/
├── main.tf                  # メインのTerraformファイル
├── variables.tf             # 変数定義
├── environments/            # 環境別設定
│   ├── dev/                 # 開発環境
│   ├── test/                # テスト環境
│   └── prod/                # 本番環境
├── modules/                 # 再利用可能なモジュール
│   ├── app-service/         # App Serviceモジュール
│   ├── azure-monitor/       # Azure Monitorモジュール
│   ├── azure-storage/       # Azure Storageモジュール
│   └── entra-id/            # Microsoft Entra IDモジュール
└── scripts/                 # ヘルパースクリプト
```

## 環境変数の設定

Terraformの実行に必要な環境変数を設定します：

```powershell
# Azure認証情報（サービスプリンシパルを使用する場合）
$env:ARM_CLIENT_ID="サービスプリンシパルのアプリケーションID"
$env:ARM_CLIENT_SECRET="サービスプリンシパルのシークレット"
$env:ARM_SUBSCRIPTION_ID="サブスクリプションID"
$env:ARM_TENANT_ID="テナントID"

# または、Azure CLIでログインしている場合は不要
```

## バックエンド設定

Terraformの状態ファイルをAzure Storageに保存するためのバックエンド設定を行います。

1. **ストレージアカウントの作成**（初回のみ）

```powershell
# リソースグループの作成
az group create --name rg-terraform-state --location japaneast

# ストレージアカウントの作成
az storage account create --name stterraformstate12345 --resource-group rg-terraform-state --location japaneast --sku Standard_LRS

# コンテナの作成
az storage container create --name tfstate --account-name stterraformstate12345
```

2. **バックエンド設定ファイルの作成**

`backend.conf`ファイルを作成し、以下の内容を記述します：

```
resource_group_name  = "rg-terraform-state"
storage_account_name = "stterraformstate12345"
container_name       = "tfstate"
key                  = "machinelog.tfstate"
```

## Terraformの実行方法

### 初期化

```powershell
# ルートディレクトリで実行
cd src/MachineLog.Infrastructure

# バックエンド設定を指定して初期化
terraform init -backend-config=backend.conf
```

### プラン作成

```powershell
# 変数ファイルを指定してプラン作成
terraform plan -var-file=terraform.tfvars -out=tfplan
```

### 適用

```powershell
# プランを適用
terraform apply tfplan

# または直接適用
terraform apply -var-file=terraform.tfvars
```

### 破棄

```powershell
# リソースの破棄
terraform destroy -var-file=terraform.tfvars
```

## 各環境での実行

現在、各環境ディレクトリ（dev, test, prod）のmain.tfファイルにはバックエンド設定が直接記述されています。より柔軟な運用のために、バックエンド設定を外部化することを推奨します。

### バックエンド設定の外部化（推奨）

各環境ディレクトリのmain.tfファイルを編集し、バックエンド設定を以下のように変更します：

```hcl
terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 2.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.0"
    }
  }
  backend "azurerm" {}
}
```

そして、各環境用のバックエンド設定ファイルを作成します：

**dev-backend.conf**
```
resource_group_name  = "rg-terraform-state"
storage_account_name = "stterraformstate12345"
container_name       = "tfstate"
key                  = "dev.terraform.tfstate"
```

**test-backend.conf**
```
resource_group_name  = "rg-terraform-state"
storage_account_name = "stterraformstate12345"
container_name       = "tfstate"
key                  = "test.terraform.tfstate"
```

**prod-backend.conf**
```
resource_group_name  = "rg-terraform-state"
storage_account_name = "stterraformstate12345"
container_name       = "tfstate"
key                  = "prod.terraform.tfstate"
```

### 開発環境

```powershell
cd src/MachineLog.Infrastructure/environments/dev
terraform init -backend-config=dev-backend.conf
terraform plan -out=tfplan
terraform apply tfplan
```

### テスト環境

```powershell
cd src/MachineLog.Infrastructure/environments/test
terraform init -backend-config=test-backend.conf
terraform plan -out=tfplan
terraform apply tfplan
```

### 本番環境

```powershell
cd src/MachineLog.Infrastructure/environments/prod
terraform init -backend-config=prod-backend.conf
terraform plan -out=tfplan
terraform apply tfplan
```

## 変数ファイルの作成

各環境ディレクトリに`terraform.tfvars`ファイルを作成し、必要な変数を設定します。

例（開発環境）：

```
resource_group_name      = "rg-machinelog-dev"
location                 = "japaneast"
environment              = "dev"
log_analytics_sku        = "PerGB2018"
log_retention_in_days    = 30
storage_account_tier     = "Standard"
storage_replication_type = "LRS"
app_service_plan_sku     = "B1"
alert_email_address      = "admin@example.com"

tags = {
  Environment = "Development"
  Project     = "MachineLog"
  Owner       = "DevOps Team"
}
```

## よくある問題とトラブルシューティング

### 認証エラー

**問題**: `Error: Error building AzureRM Client: obtain subscription() from Azure CLI: Error parsing json result from the Azure CLI: Error waiting for the Azure CLI: exit status 1`

**解決策**:
```powershell
az login
az account set --subscription "サブスクリプションID"
```

### バックエンドエラー

**問題**: `Error: Backend configuration changed`

**解決策**:
```powershell
terraform init -reconfigure -backend-config=backend.conf
```

### リソース作成エラー

**問題**: `Error: A resource with the ID "/subscriptions/.../resourceGroups/rg-machinelog-dev" already exists`

**解決策**:
```powershell
# 既存のリソースをインポート
terraform import azurerm_resource_group.this[0] /subscriptions/.../resourceGroups/rg-machinelog-dev
```

### プロバイダーエラー

**問題**: `Error: Missing required provider`

**解決策**:
```powershell
terraform init -upgrade
```

### 変数未定義エラー

**問題**: `Error: Reference to undeclared input variable`

**解決策**:
- `terraform.tfvars`ファイルに必要な変数を追加
- またはコマンドラインで変数を指定: `terraform plan -var="resource_group_name=rg-machinelog-dev"`
