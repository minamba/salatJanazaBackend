namespace QabrWebApp.Domain.Repositories
{
    public interface IUtilisateurTokenRepository
    {
        Task UpsertAsync(int utilisateurId, string expoToken);
        Task<List<string>> GetTokensByUserIdsAsync(IEnumerable<int> utilisateurIds);
    }
}
