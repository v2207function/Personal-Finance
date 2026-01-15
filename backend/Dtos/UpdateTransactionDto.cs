namespace PersonalFinanceApi.Dtos;

public class UpdateTransactionDto
{
    public decimal? Amount { get; set; }
    public string? Kind { get; set; }
    public string? Category { get; set; }
    public DateTime? Date { get; set; }
    public string? Note { get; set; }           // можно null чтобы очистить
}
