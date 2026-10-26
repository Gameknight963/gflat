// Conversion returns an independent owning string. Borrowed C-string views are
// optional operations on individual types, not part of this contract.
interface IStringConvertable
{
    immutable std::Text::String ToString();
}
