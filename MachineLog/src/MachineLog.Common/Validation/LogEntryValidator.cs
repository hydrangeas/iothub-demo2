using FluentValidation;
using MachineLog.Common.Models;

namespace MachineLog.Common.Validation;

/// <summary>
/// LogEntryのバリデーションルールを定義するクラス
/// </summary>
public class LogEntryValidator : AbstractValidator<LogEntry>
{
  /// <summary>
  /// コンストラクタ
  /// </summary>
  public LogEntryValidator()
  {
    RuleFor(x => x.Id)
        .NotEmpty().WithMessage("IDは必須です")
        .MaximumLength(50).WithMessage("IDは50文字以内である必要があります");

    RuleFor(x => x.Timestamp)
        .NotEmpty().WithMessage("タイムスタンプは必須です")
        .Must(BeValidTimestamp).WithMessage("タイムスタンプは有効な日時である必要があります");

    RuleFor(x => x.DeviceId)
        .NotEmpty().WithMessage("デバイスIDは必須です")
        .MaximumLength(100).WithMessage("デバイスIDは100文字以内である必要があります");

    RuleFor(x => x.Level)
        .NotEmpty().WithMessage("ログレベルは必須です")
        .Must(BeValidLogLevel).WithMessage("ログレベルは有効な値である必要があります");

    RuleFor(x => x.Message)
        .NotEmpty().WithMessage("メッセージは必須です");

    RuleFor(x => x.Category)
        .MaximumLength(100).WithMessage("カテゴリは100文字以内である必要があります")
        .When(x => x.Category != null);

    RuleFor(x => x.Tags)
        .Must(BeValidTags).WithMessage("タグは有効な値である必要があります")
        .When(x => x.Tags != null && x.Tags.Any());

    RuleFor(x => x.SourceFile)
        .MaximumLength(500).WithMessage("ソースファイルは500文字以内である必要があります")
        .When(x => x.SourceFile != null);

    RuleFor(x => x.Error)
        .SetValidator(new ErrorInfoValidator())
        .When(x => x.Error != null);
  }

  /// <summary>
  /// タイムスタンプが有効かどうかを検証します
  /// </summary>
  private bool BeValidTimestamp(DateTime timestamp)
  {
    // 未来の日時や、あまりにも過去の日時は無効とする
    return timestamp > new DateTime(2000, 1, 1) && timestamp <= DateTime.UtcNow.AddDays(1);
  }

  /// <summary>
  /// ログレベルが有効かどうかを検証します
  /// </summary>
  private bool BeValidLogLevel(string level)
  {
    var validLevels = new[] { "trace", "debug", "info", "information", "warn", "warning", "error", "fatal", "critical" };
    return validLevels.Contains(level.ToLower());
  }

  /// <summary>
  /// タグが有効かどうかを検証します
  /// </summary>
  private bool BeValidTags(List<string>? tags)
  {
    if (tags == null || !tags.Any())
    {
      return true;
    }

    // タグは空でなく、50文字以内であること
    return tags.All(tag => !string.IsNullOrWhiteSpace(tag) && tag.Length <= 50);
  }
}

/// <summary>
/// ErrorInfoのバリデーションルールを定義するクラス
/// </summary>
public class ErrorInfoValidator : AbstractValidator<ErrorInfo>
{
  /// <summary>
  /// コンストラクタ
  /// </summary>
  public ErrorInfoValidator()
  {
    RuleFor(x => x.Message)
        .NotEmpty().WithMessage("エラーメッセージは必須です");

    RuleFor(x => x.Code)
        .MaximumLength(50).WithMessage("エラーコードは50文字以内である必要があります")
        .When(x => x.Code != null);
  }
}