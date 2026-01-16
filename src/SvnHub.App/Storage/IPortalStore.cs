using SvnHub.Domain;

namespace SvnHub.App.Storage;

public interface IPortalStore
{
    PortalState Read();
    void Write(PortalState state);
}

