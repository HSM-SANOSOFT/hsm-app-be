import { ApiDocumentation } from '@hsm-lib/common/decorator';
import { Role } from '@hsm-lib/definitions/enums';
import { ISuccessResponse } from '@hsm-lib/definitions/interfaces';
import { Controller, Get, Param, Query } from '@nestjs/common';
import { Roles } from '../../../security/roles/roles.decorator';
import { AvailabilityService } from './availability.service';

@Controller('scheduling/availability')
export class AvailabilityController {
  constructor(private readonly availabilityService: AvailabilityService) {}
  @ApiDocumentation()
  @Roles(Role.System.Integration)
  @Get('specialties')
  getSpecialties() {
    return this.availabilityService.getSpecialties();
  }

  @ApiDocumentation()
  @Get('specialties/:specialtyId/providers')
  getProvidersBySpecialty(@Param() params: { specialtyId: string }) {
    return this.availabilityService.getProvidersBySpecialty(params);
  }

  @ApiDocumentation()
  @Get('providers/:providerId/dates')
  getProviderDates(
    @Param() params: { providerId: string },
    @Query() query: {
      dateFrom?: string;
      dateTo?: string;
    },
  ) {
    return this.availabilityService.getProviderDates(params, query);
  }
}
