namespace AgileAI.Studio.Api.Domain;

public class AgentSkillSelection
{
    public Guid AgentDefinitionId { get; set; }
    public string SkillNamesJson { get; set; } = "[]";
}
