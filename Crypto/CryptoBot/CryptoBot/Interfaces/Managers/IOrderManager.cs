using CryptoBot.Interfaces.Events;
using System.Threading.Tasks;

namespace CryptoBot.Interfaces.Managers
{
    public interface IOrderManager : IManager, IApplicationEvent
    {
        Task<bool> InvokeOrder(string symbol, MarketDirection marketDirection);
        Task FinishOrder(string symbol);
    }
}
