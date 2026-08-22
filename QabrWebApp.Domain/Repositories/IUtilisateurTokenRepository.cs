namespace QabrWebApp.Domain.Repositories
{
    public interface IUtilisateurTokenRepository
    {
        Task UpsertAsync(int utilisateurId, string expoToken);
        Task<List<string>> GetTokensByUserIdsAsync(IEnumerable<int> utilisateurIds);
        Task<List<(string Token, string Language)>> GetTokensWithLanguageByUserIdsAsync(IEnumerable<int> utilisateurIds);
        Task<List<string>> GetAllTokensPagedAsync(int skip, int pageSize);
    }
}
