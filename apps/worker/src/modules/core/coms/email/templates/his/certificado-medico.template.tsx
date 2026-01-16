import { CertificadoMedicoTemplateDto } from '@hsm-lib/definitions/dtos';

export function CertificadoMedicoTemplate(
  data: CertificadoMedicoTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        Estimad@ paciente: <i>{data.patientName}</i>
        <br />
        Le saludamos del Hospital Santamaria.
        <br />
        <br />
        Adjunto se encuentra el Certificado Medico solicitado.
        <br />
        <br />
        Atentamente
        <br />
        Admisiones
        <br />
        Celular 0981764904
        <br />
        <br />
        NOTA:{' '}
        <i>
          Los documentos de este correo deben subirse via web al IESS, si usted{' '}
          lo imprime pierde validez ya que es un documento electrónico.
        </i>
      </p>
    </>
  );
}
