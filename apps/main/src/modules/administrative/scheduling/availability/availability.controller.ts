import { ApiDocumentation, Public } from '@hsm-lib/common/decorator';
import { Role } from '@hsm-lib/definitions/enums';
import { ISuccessResponse } from '@hsm-lib/definitions/interfaces';
import { Controller, Get } from '@nestjs/common';
import { Roles } from '../../../security/roles/roles.decorator';

@Controller('scheduling/availability')
export class AvailabilityController {
  @ApiDocumentation()
  @Public()
  @Roles(Role.System.Integration, Role.Administrative.Receptionist)
  @Get()
  availability() {
    return;
  }
}
