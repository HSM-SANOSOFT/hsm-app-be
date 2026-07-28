namespace Hsm.Contract.Tests.Fhir;

/// <summary>The U16 FHIR suite host (hsm_fhir_test).</summary>
public sealed class FhirApiFactory : ContractApiFactory
{
    protected override string DatabaseName => "hsm_fhir_test";
}
