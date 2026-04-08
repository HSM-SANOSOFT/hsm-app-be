import { ApiDocumentation } from '@hsm-app/server/decorator';
import { Roles } from '@hsm-app/server/modules/security/roles/roles.decorator';
import { Controller, Get, Post } from '@nestjs/common';

@Controller('patients')
export class PatientsController {
  // TODO: Add endpoint implementations

  @ApiDocumentation()
  @Roles()
  @Post('create')
  async createPatient() {
    // Implementation for creating a patient
  }

  @ApiDocumentation()
  @Roles()
  @Post('update')
  async updatePatient() {
    // Implementation for updating a patient
  }

  @ApiDocumentation()
  @Roles()
  @Post('delete')
  async deletePatient() {
    // Implementation for deleting a patient
  }

  @ApiDocumentation()
  @Roles()
  @Get(':id')
  async getPatients() {
    // Implementation for retrieving patients
  }
}
