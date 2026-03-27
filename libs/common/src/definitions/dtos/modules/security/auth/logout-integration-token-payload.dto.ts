import { ApiProperty, ApiSchema } from '@nestjs/swagger';
import { IsNotEmpty, IsString } from 'class-validator';

@ApiSchema({ name: 'Logout Integration Token Payload' })
export class LogoutIntegrationTokenPayloadDto {
  @ApiProperty({ required: true, description: 'Either Token is required' })
  @IsString()
  @IsNotEmpty()
  token: string;
}
