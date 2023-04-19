using Bybit.Net.Enums;
using CryptoBot.Interfaces.Events;
using System.Threading.Tasks;

namespace CryptoBot.Interfaces.Managers
{
    public interface IOrderManager : IManager, IWebSocketEvent
    {
        Task<bool> InvokeOrder(string symbol, OrderSide orderSide);
        Task FinishOrder(string symbol);
    }
}
