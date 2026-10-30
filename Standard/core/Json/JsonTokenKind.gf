using Standard::Text;
namespace Standard::Json;

public enum JsonTokenKind
{
    None, StartObject, EndObject, StartArray, EndArray,
    PropertyName, String, Number, True, False, Null
}
