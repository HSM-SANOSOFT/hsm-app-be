import { PinPurpose } from '@hsm-lib/definitions/enums';
import { ApiProperty, ApiSchema } from '@nestjs/swagger';
import { IsEnum, IsNotEmpty, IsString } from 'class-validator';

@ApiSchema({ name: 'PIN Generation Payload' })
export class PinGenerationPayloadDto {
  @IsNotEmpty()
  @IsEnum(PinPurpose)
  @ApiProperty({
    description: 'Tipo de PIN a generar',
    required: true,
    enum: PinPurpose,
    examples: [PinPurpose.EMAIL_VERIFICATION, PinPurpose.PASSWORD_RESET],
  })
  purpose: PinPurpose;

  @IsNotEmpty()
  @IsString()
  @ApiProperty({
    description: 'Identificador único (email, userId, phone, etc)',
    required: true,
    examples: ['example@example.com', '0996000937'],
  })
  target: string;
}
