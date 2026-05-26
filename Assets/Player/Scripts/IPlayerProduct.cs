namespace Player
{
    public interface IPlayerProduct
    {
        string ProductName { get; set; }
        void Initialize(PlayerConfigSO  config);
    }
}