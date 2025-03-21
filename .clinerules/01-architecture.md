# システムアーキテクチャ

本システムは以下の主要コンポーネントで構成されています：

- **MachineLog.Collector**: .NET 8.0 Worker Serviceベースのログ収集サービス
  - 産業機器からログファイルを監視・収集
  - IoT HubのUploadModuleLogsを使用してBlobストレージにアップロード
  - 効率的なバッチ処理とリトライメカニズムを実装

- **MachineLog.Monitor**: ASP.NET 8.0 Webアプリケーション
  - Blazor WebAssemblyベースのSPA
  - Azure Storage APIを使用してログデータにアクセス
  - リアルタイム分析と可視化機能を提供

- **MachineLog.Common**: 共通ライブラリ
  - すべてのコンポーネントで共有されるモデルとユーティリティ
  - ログエントリの検証と処理のための標準化されたロジック

- **MachineLog.Infrastructure**: インフラストラクチャ定義
  - TerraformによるAzureリソースのプロビジョニング
  - 環境ごとの構成管理（開発、テスト、本番）

## データフロー

1. 産業機器がログファイルを生成
2. MachineLog.Collectorがファイル変更を検出
3. ログファイルをJSON Lines形式で解析
4. IoT HubのUploadModuleLogsを使用してBlobストレージにアップロード
5. ログデータがBlobストレージに保存
6. MachineLog.MonitorがAzure Storage APIを使用してデータにアクセス
7. Webインターフェースでユーザーにデータを表示

## 非機能要件

- **パフォーマンス**:
  - 高スループット処理（1秒あたり最大10,000ログエントリ）
  - 低レイテンシ応答（ログ収集から保存まで5秒以内）
  - 効率的なI/O操作（非同期処理、バッファリング）
  - CancellationTokenによる処理の適切な中断

- **スケーラビリティ**:
  - 水平スケーリング対応（コンテナ化、ステートレス設計）
  - 自動スケーリングルールの実装（CPU使用率、メモリ消費、キュー長に基づく）
  - マイクロサービスアーキテクチャの採用
  - 負荷分散戦略（ラウンドロビン、最小接続数）

- **可用性**:
  - 99.99%以上の稼働時間（年間ダウンタイム52分以内）
  - 冗長構成（複数リージョン、アベイラビリティゾーン）
  - 自動フェイルオーバーメカニズム
  - ヘルスチェックとサーキットブレーカーパターンの実装

- **セキュリティアーキテクチャ**:
  - ゼロトラストセキュリティモデルの採用
  - すべての通信の暗号化（TLS 1.3）
  - 最小権限の原則に基づくIAM設計
  - 侵入検知システム（IDS）と侵入防止システム（IPS）の実装
  - データ暗号化（保存時および転送時）
  - セキュアなCI/CDパイプライン（コード署名、脆弱性スキャン）

- **障害回復（DR）戦略**:
  - 地理的に分散したバックアップ（GRS）
  - RTO（目標復旧時間）: 4時間以内
  - RPO（目標復旧時点）: 15分以内
  - 定期的なDRテストと演習
  - 自動フェイルオーバーと手動フェイルバック
  - 詳細な障害復旧計画とドキュメント

- **設定管理**:
  - 環境ごとの設定（appsettings.json）
  - Terraformによる環境間の一貫性確保
  - シークレット管理（Azure Key Vault）
  - 設定の動的リロード機能
