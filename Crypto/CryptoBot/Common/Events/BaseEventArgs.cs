
namespace Common.Events
{
    public abstract class BaseEventArgs
    {
        public MessageType MessageType { get; set; }
        public string Message { get; set; }

        public BaseEventArgs(MessageType messageType, string message)
        {
            this.MessageType = messageType;
            this.Message = message;
        }

        public override string ToString()
        {
            return $"[{this.MessageType}] {this.Message}";
        }
    }
}
