using System;

namespace QabrWebApp.Dal.Entities;

public class LikeCommentaire
{
    public int Id { get; set; }
    public int CommentaireId { get; set; }
    public int? UtilisateurId { get; set; }
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;

    public virtual CommentaireJanaza CommentaireJanaza { get; set; } = null!;
}
