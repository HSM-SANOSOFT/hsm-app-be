import { ITokens } from '@hsm-lib/definitions/interfaces';
import { ApiProperty, ApiSchema } from '@nestjs/swagger';

@ApiSchema({ name: 'Access and Refresh Token' })
export class TokensDto implements ITokens {
  @ApiProperty({ description: 'Authorization token', type: 'string' })
  access_token: string;

  @ApiProperty({ description: 'Authorization token', type: 'string' })
  refresh_token: string;
}
