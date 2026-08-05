namespace Hsm.Api.Tests.System;

public sealed class SystemFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_system_status";
}
