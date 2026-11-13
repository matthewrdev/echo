namespace Echo.Projects;

public interface IProjectStoreFactory
{
    IProjectStore Open(string root);
}
