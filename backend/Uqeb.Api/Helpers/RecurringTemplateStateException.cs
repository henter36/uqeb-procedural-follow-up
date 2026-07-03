namespace Uqeb.Api.Helpers;

public abstract class RecurringTemplateStateException : Exception
{
    protected RecurringTemplateStateException(string message) : base(message) { }
}

public sealed class RecurringTemplatePausedException : RecurringTemplateStateException
{
    public RecurringTemplatePausedException() : base("القالب الدوري موقوف مؤقتًا.") { }
}

public sealed class RecurringTemplateTerminatedException : RecurringTemplateStateException
{
    public RecurringTemplateTerminatedException() : base("تم إنهاء هذا الالتزام الدوري ولا يمكن إنشاء فترات جديدة منه.") { }
}

public sealed class RecurringTemplatePeriodOutOfRangeException : RecurringTemplateStateException
{
    public RecurringTemplatePeriodOutOfRangeException() : base("الفترة المطلوبة خارج نطاق سريان الالتزام الدوري.") { }
}

public sealed class RecurringTemplatePeriodAlreadyGeneratedException : Exception
{
    public RecurringTemplatePeriodAlreadyGeneratedException(int existingTransactionId)
        : base("تم إنشاء معاملة لهذه الفترة مسبقًا.")
    {
        ExistingTransactionId = existingTransactionId;
    }

    public int ExistingTransactionId { get; }
}
