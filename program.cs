using System;

// ---------- Behaviors (delegates) ----------
Func<decimal, decimal> percent = a => a * 0.02m;  
Func<decimal, decimal> flat    = _ => 5m;         

// ---------- Demo ----------
var card     = new CardChannel(maxAmount: 1000m);
var transfer = new TransferChannel(minAmount: 50m);

var start = new PaymentBalance("U-001", 100m);
Show("Start", start);

// 1. Card + percent, then a second deposit using the balance returned by the first
var (afterCard, cardFee) = start.Deposit(200m, card, percent);
Show($"After card 200 (percent, fee {cardFee})", afterCard);

var (afterTransfer, transferFee) = afterCard.Deposit(300m, transfer, flat);
Show($"After transfer 300 (flat, fee {transferFee})", afterTransfer);

// 2. Earlier balances are not changed
Show("start (unchanged)", start);
Show("afterCard (unchanged)", afterCard);

// 3. Value equality with ==
var expected = new PaymentBalance("U-001", 100m + 200m - 4m);
Console.WriteLine($"afterCard == new PaymentBalance(\"U-001\", 296m): {afterCard == expected}");
Console.WriteLine($"start == afterCard: {start == afterCard}");
Console.WriteLine($"ReferenceEquals(afterCard, expected): {ReferenceEquals(afterCard, expected)}");

// 4. Other combinations: card + flat, transfer + percent
var (b3, f3) = start.Deposit(500m, card, flat);
Show($"Card + flat 500 (fee {f3})", b3);
var (b4, f4) = start.Deposit(100m, transfer, percent);
Show($"Transfer + percent 100 (fee {f4})", b4);

// 5. Channel rules reject invalid deposits
TryDeposit(start, 5000m, card, percent);      // above card limit
TryDeposit(start, 10m, transfer, percent);    // below transfer minimum

// 6. The problem case: start.Deposit(3m, card, flat)
//    Without validation: commission (5) > amount (3) => balance becomes 100 + 3 - 5 = 98 (it DECREASES).
//    With the validation in PaymentBalance.Validate the call is rejected:
TryDeposit(start, 3m, card, flat);

// ---------- Helpers ----------
static void Show(string label, PaymentBalance b) =>
    Console.WriteLine($"{label,-40} Owner={b.OwnerID}, Amount={b.Amount}");

static void TryDeposit(PaymentBalance b, decimal amount, IPaymentChannel ch, Func<decimal, decimal> policy)
{
    try
    {
        var (updated, fee) = b.Deposit(amount, ch, policy);
        Show($"OK {ch.Name} {amount} (fee {fee})", updated);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        Console.WriteLine($"REJECTED {ch.Name} {amount}: {ex.Message}");
    }
}

// ---------- Types ----------
public interface IPaymentChannel
{
    string Name { get; }
    bool IsAllowed(decimal amount);
}

// Card: does not allow operations above a limit (limit set by the caller)
public sealed class CardChannel : IPaymentChannel
{
    private readonly decimal _maxAmount;
    public CardChannel(decimal maxAmount)
    {
        if (maxAmount <= 0) throw new ArgumentOutOfRangeException(nameof(maxAmount));
        _maxAmount = maxAmount;
    }
    public string Name => "Card";
    public bool IsAllowed(decimal amount) => amount <= _maxAmount;
}

// Transfer: requires a minimal amount (set by the caller)
public sealed class TransferChannel : IPaymentChannel
{
    private readonly decimal _minAmount;
    public TransferChannel(decimal minAmount)
    {
        if (minAmount < 0) throw new ArgumentOutOfRangeException(nameof(minAmount));
        _minAmount = minAmount;
    }
    public string Name => "Transfer";
    public bool IsAllowed(decimal amount) => amount >= _minAmount;
}

public record PaymentBalance(string OwnerID, decimal Amount)
{
    public (PaymentBalance Updated, decimal Commission) Deposit(
        decimal amount, IPaymentChannel channel, Func<decimal, decimal> commissionPolicy)
    {
        Validate(amount, channel, commissionPolicy);

        var commission = commissionPolicy(amount);

        // The commission must be valid and must not eat the whole deposit
        if (commission < 0)
            throw new InvalidOperationException("Commission cannot be negative.");
        if (commission >= amount)
            throw new InvalidOperationException(
                $"Commission ({commission}) is not less than the deposit ({amount}); the balance would not grow.");

        return (this with { Amount = Amount + amount - commission }, commission);
    }

    // Separate validation of the input (decomposition)
    private static void Validate(decimal amount, IPaymentChannel channel, Func<decimal, decimal> policy)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(policy);

        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Deposit must be positive.");
        if (!channel.IsAllowed(amount))
            throw new InvalidOperationException($"{channel.Name} does not allow the amount {amount}.");
    }
}