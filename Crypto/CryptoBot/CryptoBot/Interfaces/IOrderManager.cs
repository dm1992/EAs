using System.Threading.Tasks;

namespace CryptoBot.Interfaces
{
    public interface IOrderManager : IAPISubscription
    {
        Task<bool> InvokeOrderAsync(string symbol);
        Task FinishOrderAsync(string symbol);
    }
}
