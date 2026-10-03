/*
  Utilizador SQL só para o Portal Ordem MSS, com o mínimo de permissões:
  - ler Artigo e Familias (PRIMSS2CLO)
  - alterar só a coluna CDU_MSS_ORDEM de Artigo

  Correr UMA vez no SQL Server Management Studio, ligado ao 192.168.1.170\PRIMAVERA
  como administrador (sa ou equivalente). Trocar a password antes de correr.
  Depois, pôr no settings.json do portal:
    "ConnectionString": "Server=192.168.1.170\\PRIMAVERA;Database=PRIMSS2CLO;User Id=portal_ordem_mss;Password=<a mesma>;TrustServerCertificate=True"
  e correr tools\ATUALIZAR-PORTAL.ps1 (ou reiniciar o serviço MssPortalOrdem).

  Para desfazer: DROP USER portal_ordem_mss (em PRIMSS2CLO) e DROP LOGIN portal_ordem_mss.
*/
USE master;
CREATE LOGIN portal_ordem_mss WITH PASSWORD = N'TROCAR-por-uma-password-forte', CHECK_POLICY = ON, DEFAULT_DATABASE = PRIMSS2CLO;
GO

USE PRIMSS2CLO;
CREATE USER portal_ordem_mss FOR LOGIN portal_ordem_mss;
GRANT SELECT ON dbo.Artigo TO portal_ordem_mss;
GRANT SELECT ON dbo.Familias TO portal_ordem_mss;
GRANT UPDATE (CDU_MSS_ORDEM) ON dbo.Artigo TO portal_ordem_mss;
GO
