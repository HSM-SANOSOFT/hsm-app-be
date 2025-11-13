import { Role } from '@hsm-lib/definitions/enums';
import { Roles } from '@hsm-lib/definitions/types';
import { ApiProperty, ApiSchema } from '@nestjs/swagger';

@ApiSchema({ name: 'Signed User Profile' })
export class SignedUserProfileDto {
  @ApiProperty({ description: 'Unique user identifier' })
  id!: string;

  @ApiProperty({ description: 'Username', example: 'raul123' })
  username!: string;

  @ApiProperty({ description: 'Email address', example: 'raul@example.com' })
  email!: string;

  @ApiProperty({ description: 'First name', example: 'Raul' })
  firstName!: string;

  @ApiProperty({ description: 'First last name', example: 'Santamaria' })
  firstLastName!: string;

  @ApiProperty({
    description: 'Assigned roles',
    isArray: true,
    enum: Role,
    example: [Role.System.Admin],
  })
  roles!: Roles[];

  @ApiProperty({
    description: 'Issued-at timestamp (JWT)',
    example: 1731500000,
  })
  iat!: number;

  @ApiProperty({
    description: 'Expiration timestamp (JWT)',
    example: 1731503600,
  })
  exp!: number;
}

@ApiSchema({ name: 'Signed Integration User Profile' })
export class SignedIntegrationProfileDto {
  @ApiProperty({ description: 'Unique integration identifier' })
  id!: string;

  @ApiProperty({
    description: 'Integration name',
    example: 'SCHEDULING_SERVICE',
  })
  name!: string;

  @ApiProperty({
    description: 'Assigned roles',
    isArray: true,
    enum: Role,
    example: [Role.System.Integration],
  })
  roles!: Roles[];

  @ApiProperty({
    description: 'Issued-at timestamp (JWT)',
    example: 1731500000,
  })
  iat!: number;

  @ApiProperty({
    description: 'Expiration timestamp (JWT)',
    example: 1731503600,
  })
  exp!: number;
}
