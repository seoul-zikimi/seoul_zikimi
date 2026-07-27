namespace Kamgam.HitMe
{
    public interface IProjectile
    {
        float Time { get; }
        void Reset(bool resetConfig);
        T GetConfig<T>() where T : class;
    }
}