import { BienvenidaTemplateDto } from '@hsm-lib/common/definitions/dtos';

export function BienvenidaTemplate(
  _data: BienvenidaTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>Estimad@ Pacientes</p>
      <p>¡Bienvenidos al Hospital Santamaría!</p>
    </>
  );
}
