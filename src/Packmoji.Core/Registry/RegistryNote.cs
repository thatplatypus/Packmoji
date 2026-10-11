namespace Packmoji.Core.Registry
{
    /// <summary>
    /// One thing a run has to say, as a line that is fit to print. Its text is the run's own words,
    /// with names of listed repositories, tags that were read and found well formed, and names that a
    /// manifest gave and that were found well formed: nothing else from inside an archive is in it.
    /// </summary>
    public sealed record RegistryNote(RegistryNoteKind Kind, string Text);
}
