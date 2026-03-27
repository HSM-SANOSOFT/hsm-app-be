import { ApiProperty, ApiSchema } from '@nestjs/swagger';
import { IsNotEmpty, IsString } from 'class-validator';

@ApiSchema({ name: 'Delete User Payload' })
export class DeleteUserPayloadDto {
  @IsNotEmpty()
  @IsString()
  @ApiProperty({ required: true })
  id!: string;
}
