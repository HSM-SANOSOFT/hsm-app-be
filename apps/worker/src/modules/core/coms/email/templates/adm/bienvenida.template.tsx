import { BienvenidaTemplateDto } from '@hsm-lib/definitions/dtos';

export function BienvenidaTemplate(
  data: BienvenidaTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>Estimad@ Pacientes</p>
      <p>¡Bienvenidos al Hospital Santamaría!</p>
    </>
  );
}
