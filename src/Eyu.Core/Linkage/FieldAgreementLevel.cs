namespace Eyu.Core.Linkage;

/// <summary>
/// Whether two records' values for one field, after normalization, are the same. A field missing
/// or blank on either side never produces a level — see <see cref="FieldComparator.Compare"/>.
/// </summary>
public enum FieldAgreementLevel
{
    Disagree,
    Agree,
}
