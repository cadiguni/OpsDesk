using System.ComponentModel.DataAnnotations;

namespace OpsDesk.Infrastructure.Security;

public class DataProtectionSettings
{
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Onde ficam as chaves que cifram as credenciais guardadas no banco.
    ///
    /// Obrigatório e sem valor padrão, de propósito. O padrão do ASP.NET, num contêiner,
    /// é uma pasta dentro da imagem: o próximo deploy gera chaves novas, o client secret
    /// gravado deixa de ser decifrável, e o e-mail para sem erro que aponte a causa. Em
    /// contêiner, isto aponta para um volume.
    /// </summary>
    [Required(ErrorMessage = "DataProtection:KeysPath é obrigatório: aponte para um diretório persistente (em contêiner, um volume).")]
    public string KeysPath { get; set; } = null!;
}
